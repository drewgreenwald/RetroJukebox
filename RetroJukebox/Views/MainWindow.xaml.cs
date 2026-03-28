using Microsoft.Win32;
using RetroJukebox.Controls;
using RetroJukebox.Models;
using RetroJukebox.Services;
using RetroJukebox.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RetroJukebox.Views;

public partial class MainWindow : Window
{
    private MainViewModel VM => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Loaded += (_, _) =>
        {
            VM.PropertyChanged += (__, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.RightPanelTab))
                    SetRightTab(VM.RightPanelTab);
            };
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VM.StatusText = $"Library: {App.LibraryService.Tracks.Count:N0} tracks";
        SetRightTab("Queue"); // apply initial tab highlight
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // SpectrumDataSource is a Func<float[]> — cannot be data-bound, must be wired in code-behind
        SpectrumViz.SpectrumDataSource = App.AudioService.SpectrumDataSource;
    }

    // ── Waveform scrubber seek ─────────────────────────────────────────────
    private void WaveformScrubber_Seek(object sender, WaveformSeekEventArgs e)
        => VM.SeekToFraction(e.Fraction);

    // ── File adding ────────────────────────────────────────────────────────
    private async void AddFilesClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title     = "Add Audio Files",
            Multiselect = true,
            Filter    = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.aac;*.m4a;*.mp4|All Files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        VM.StatusText = $"Adding {dlg.FileNames.Length} files…";
        foreach (var file in dlg.FileNames)
        {
            if (App.LibraryService.Tracks.Any(t =>
                string.Equals(t.FilePath, file, StringComparison.OrdinalIgnoreCase))) continue;
            var track = App.LibraryService.ReadMetadata(file);
            App.LibraryService.Tracks.Add(track);
        }
        VM.StatusText = $"Library: {App.LibraryService.Tracks.Count:N0} tracks";
    }

    // ── Seek scrub ────────────────────────────────────────────────────────
    private void Seek_PreviewMouseDown(object sender, MouseButtonEventArgs e) => VM.BeginScrub();
    private void Seek_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s) VM.EndScrub(s.Value);
    }

    // ── Track list double-click ───────────────────────────────────────────
    private void TrackList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TrackListView.SelectedItem is not Track track) return;

        App.PlaylistService.Enqueue(track);

        // Case 1: queue was empty before this track was added — start playing immediately
        // Case 2/3: queue already had content (playing or paused) — just appended, don't interrupt
        if (!App.AudioService.IsPlaying && App.PlaylistService.Queue.Count == 1)
        {
            App.PlaylistService.PlayAt(0);
            App.AudioService.Play(track);
            VM.CurrentTrack = track;
        }
    }

    // ── Queue — open file from disk (transient, not added to library) ─────
    private void QueueOpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Open File for Playback",
            Multiselect = true,
            Filter      = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.aac;*.m4a;*.mp4;*.wv;*.ape|All Files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        Track? firstNew = null;
        foreach (var path in dlg.FileNames)
        {
            // Build a track object from metadata but mark it transient — never goes to the DB
            var track = App.LibraryService.ReadMetadata(path);
            track.IsTransient = true;
            App.PlaylistService.Enqueue(track);
            firstNew ??= track;
        }

        // If nothing is currently playing, start the first opened file immediately
        if (firstNew != null && App.AudioService.State != Services.PlaybackState.Playing)
        {
            var idx = App.PlaylistService.Queue.IndexOf(firstNew);
            App.PlaylistService.PlayAt(idx);
            App.AudioService.Play(firstNew);
            VM.CurrentTrack = firstNew;
        }
    }
    private Point  _dragStartPoint;
    private bool   _isDraggingQueue;
    private Track? _draggedTrack;

    private void QueueList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint  = e.GetPosition(null);
        _isDraggingQueue = false;
        _draggedTrack    = null;
    }

    private void QueueList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDraggingQueue) return;

        var pos   = e.GetPosition(null);
        var delta = _dragStartPoint - pos;

        // Only start drag after moving a few pixels (avoids accidental drags on clicks)
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (QueueList.SelectedItem is not Track track) return;

        _isDraggingQueue = true;
        _draggedTrack    = track;
        DragDrop.DoDragDrop(QueueList, track, DragDropEffects.Move);
        _isDraggingQueue = false;
    }

    private void QueueList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Track))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void QueueList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(Track))) return;
        if (e.Data.GetData(typeof(Track)) is not Track dragged) return;

        // Find which item we dropped onto
        var target = GetQueueItemAt(e.GetPosition(QueueList));
        if (target == null || target == dragged) return;

        var oldIdx = App.PlaylistService.Queue.IndexOf(dragged);
        var newIdx = App.PlaylistService.Queue.IndexOf(target);
        if (oldIdx < 0 || newIdx < 0) return;

        App.PlaylistService.MoveTrack(oldIdx, newIdx);
        QueueList.SelectedItem = dragged; // keep dragged item selected after move
        e.Handled = true;
    }

    /// <summary>Hit-test the ListBox to find which Track item is at a given point.</summary>
    private Track? GetQueueItemAt(Point position)
    {
        var element = QueueList.InputHitTest(position) as DependencyObject;
        while (element != null)
        {
            if (element is ListBoxItem item && item.DataContext is Track t)
                return t;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    // ── Queue context menu + keyboard ─────────────────────────────────────
    private void QueueCtxPlay(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not Track track) return;
        var idx = App.PlaylistService.Queue.IndexOf(track);
        App.PlaylistService.PlayAt(idx);
        App.AudioService.Play(track);
        VM.CurrentTrack = track;
    }

    private void QueueCtxMoveUp(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not Track track) return;
        var idx = App.PlaylistService.Queue.IndexOf(track);
        if (idx > 0) App.PlaylistService.MoveTrack(idx, idx - 1);
    }

    private void QueueCtxMoveDown(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not Track track) return;
        var idx = App.PlaylistService.Queue.IndexOf(track);
        if (idx < App.PlaylistService.Queue.Count - 1)
            App.PlaylistService.MoveTrack(idx, idx + 1);
    }

    private void QueueCtxRemove(object sender, RoutedEventArgs e)
    {
        foreach (Track track in QueueList.SelectedItems.Cast<Track>().ToList())
            App.PlaylistService.Remove(track);
    }

    private void QueueList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            foreach (Track track in QueueList.SelectedItems.Cast<Track>().ToList())
                App.PlaylistService.Remove(track);
            e.Handled = true;
        }
    }

    // ── Queue double-click ────────────────────────────────────────────────
    private void Queue_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (QueueList.SelectedItem is not Track track) return;
        var idx = App.PlaylistService.Queue.IndexOf(track);
        App.PlaylistService.PlayAt(idx);
        App.AudioService.Play(track);
        VM.CurrentTrack = track;
    }

    // ── Context menu ──────────────────────────────────────────────────────
    private void CtxPlay(object sender, RoutedEventArgs e)
    {
        if (TrackListView.SelectedItem is not Track track) return;
        App.PlaylistService.Clear();
        var tracks = VM.DisplayedTracks.ToList();
        App.PlaylistService.EnqueueRange(tracks);
        var idx = tracks.IndexOf(track);
        App.PlaylistService.PlayAt(Math.Max(0, idx));
        App.AudioService.Play(track);
        VM.CurrentTrack = track;
    }

    private void CtxAddToQueue(object sender, RoutedEventArgs e)
    {
        foreach (Track track in TrackListView.SelectedItems.Cast<Track>().ToList())
            App.PlaylistService.Enqueue(track);
    }

    private void CtxEditMetadata(object sender, RoutedEventArgs e)
    {
        if (TrackListView.SelectedItem is Track track)
            new MetadataEditorWindow(track) { Owner = this }.ShowDialog();
    }

    private void CtxRemove(object sender, RoutedEventArgs e)
    {
        if (TrackListView.SelectedItem is Track track)
            App.LibraryService.RemoveTrack(track);
    }

    // ── Tree navigation ────────────────────────────────────────────────────
    private void TreeNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[TreeNav_SelectionChanged] AddedItems={e.AddedItems.Count}");
        if (e.AddedItems.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TreeNav_SelectionChanged] Item type={e.AddedItems[0]?.GetType().Name} value='{e.AddedItems[0]}'");
            if (e.AddedItems[0] is string item)
                VM.SelectTreeItemCommand.Execute(item);
        }
    }

    // ── Settings / Metadata editor ─────────────────────────────────────────
    private void OpenMetadataEditor(object sender, RoutedEventArgs e)
    {
        if (TrackListView.SelectedItem is Track track)
            new MetadataEditorWindow(track) { Owner = this }.ShowDialog();
        else
            MessageBox.Show("Select a track in the library first.", "Metadata Editor",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
        => new SettingsWindow { Owner = this }.ShowDialog();

    // ── Repeat cycle ──────────────────────────────────────────────────────
    private void RepeatBtn_Click(object sender, RoutedEventArgs e)
    {
        VM.RepeatMode = VM.RepeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All  => RepeatMode.One,
            RepeatMode.One  => RepeatMode.None,
            _               => RepeatMode.None
        };
    }

    // ── Phase 7: Right panel tab switching ───────────────────────────────
    private void TabQueue_Click(object sender, RoutedEventArgs e)     => SetRightTab("Queue");
    private void TabPlaylists_Click(object sender, RoutedEventArgs e) => SetRightTab("Playlists");

    private void SetRightTab(string tab)
    {
        bool isQueue = tab == "Queue";
        QueueList.Visibility       = isQueue ? Visibility.Visible   : Visibility.Collapsed;
        PlaylistsPanel.Visibility  = isQueue ? Visibility.Collapsed : Visibility.Visible;
        QueueActionButtons.Visibility = isQueue ? Visibility.Visible : Visibility.Collapsed;

        // Bold the active tab label
        TabQueueLabel.Foreground     = isQueue
            ? (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
            : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        TabPlaylistsLabel.Foreground = !isQueue
            ? (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
            : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
    }

    // ── Phase 7: Save queue as playlist ──────────────────────────────────
    private void SavePlaylist_Click(object sender, RoutedEventArgs e)
        => TrySavePlaylist();

    private void PlaylistNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TrySavePlaylist();
    }

    private void TrySavePlaylist()
    {
        var name = PlaylistNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a playlist name.", "Save Playlist",
                MessageBoxButton.OK, MessageBoxImage.Information);
            PlaylistNameBox.Focus();
            return;
        }

        // If a playlist with this name already exists, confirm overwrite
        var existing = VM.SavedPlaylists.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var result = MessageBox.Show(
                $"A playlist named \"{name}\" already exists. Overwrite it?",
                "Save Playlist", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        VM.SaveQueueAsPlaylistCommand.Execute(name);
        PlaylistNameBox.Clear();
    }

    // ── Phase 7: Queue context → Save as Playlist ────────────────────────
    private void QueueCtxSaveAsPlaylist(object sender, RoutedEventArgs e)
    {
        SetRightTab("Playlists");
        PlaylistNameBox.Focus();
    }

    // ── Phase 7: Playlist list item buttons ──────────────────────────────
    private void PlaylistLoad_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SavedPlaylist pl)
            VM.LoadPlaylistCommand.Execute(pl);
    }

    private void PlaylistDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SavedPlaylist pl) return;
        var result = MessageBox.Show($"Delete playlist \"{pl.Name}\"?",
            "Delete Playlist", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            VM.DeletePlaylistCommand.Execute(pl);
    }

    // ── Phase 7: Playlist context menu ───────────────────────────────────
    private void PlaylistCtxLoad(object sender, RoutedEventArgs e)
    {
        if (PlaylistsList.SelectedItem is SavedPlaylist pl)
            VM.LoadPlaylistCommand.Execute(pl);
    }

    private void PlaylistCtxDelete(object sender, RoutedEventArgs e)
    {
        if (PlaylistsList.SelectedItem is not SavedPlaylist pl) return;
        var result = MessageBox.Show($"Delete playlist \"{pl.Name}\"?",
            "Delete Playlist", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            VM.DeletePlaylistCommand.Execute(pl);
    }
}
