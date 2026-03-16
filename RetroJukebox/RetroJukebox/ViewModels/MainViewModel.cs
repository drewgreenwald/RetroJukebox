using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroJukebox.Audio.Waveform;
using RetroJukebox.Models;
using RetroJukebox.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Data;

namespace RetroJukebox.ViewModels;

public enum LibraryView { AllTracks, Artists, Albums, Genres }

public partial class MainViewModel : ObservableObject
{
    private readonly AudioService _audio;
    private readonly LibraryService _library;
    private readonly PlaylistService _playlist;

    // ── Sort state ────────────────────────────────────────────────────────
    private string _sortColumn = "DisplayTitle";
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    // ── Observable properties ─────────────────────────────────────────────
    [ObservableProperty] private Track? _currentTrack;
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private float _volume = 0.75f;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isScrubbing;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private LibraryView _currentView = LibraryView.AllTracks;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string? _selectedArtist;
    [ObservableProperty] private string? _selectedAlbum;
    [ObservableProperty] private string? _selectedGenre;
    [ObservableProperty] private ObservableCollection<string> _treeItems = [];

    // ── Phase 4: waveform scrubber ────────────────────────────────────────
    [ObservableProperty] private (float Min, float Max)[]? _waveformData;
    [ObservableProperty] private double _waveformProgress;

    private CancellationTokenSource? _waveformCts;

    // ── Displayed tracks — plain ObservableCollection, bound directly ─────
    // Fixed reference — ListView binds to this instance permanently
    public ObservableCollection<Track> DisplayedTracks { get; } = new ObservableCollection<Track>();

    // Sort indicators
    [ObservableProperty] private string _sortIndicatorTitle    = "▲";
    [ObservableProperty] private string _sortIndicatorArtist   = "";
    [ObservableProperty] private string _sortIndicatorAlbum    = "";
    [ObservableProperty] private string _sortIndicatorGenre    = "";
    [ObservableProperty] private string _sortIndicatorYear     = "";
    [ObservableProperty] private string _sortIndicatorTrackNum = "";
    [ObservableProperty] private string _sortIndicatorDuration = "";

    private System.Windows.Threading.DispatcherTimer? _positionTimer;

    public ObservableCollection<Track> Queue => _playlist.Queue;
    public LibraryService Library => _library;
    public PlaylistService Playlist => _playlist;
    public AudioService Audio => _audio;
    public int DisplayedCount => DisplayedTracks.Count;

    // ── Phase 4 ───────────────────────────────────────────────────────────
    public EqualizerViewModel Equalizer { get; private set; } = null!;

    public bool Shuffle
    {
        get => _playlist.Shuffle;
        set { _playlist.Shuffle = value; OnPropertyChanged(); }
    }

    public RepeatMode RepeatMode
    {
        get => _playlist.RepeatMode;
        set { _playlist.RepeatMode = value; OnPropertyChanged(); }
    }

    /// <summary>Passes through to AudioService.PlaybackQuality — bound to the lo-fi quality ComboBox.</summary>
    public int PlaybackQuality
    {
        get => _audio.PlaybackQuality;
        set { _audio.PlaybackQuality = value; OnPropertyChanged(); }
    }

    // ── Constructor ───────────────────────────────────────────────────────
#pragma warning disable CS8618 // Services assigned below; early-return guard is intentional
    public MainViewModel()
#pragma warning restore CS8618
    {
        if (App.AudioService == null || App.LibraryService == null || App.PlaylistService == null)
            return;

        _audio    = App.AudioService;
        _library  = App.LibraryService;
        _playlist = App.PlaylistService;

        _audio.PropertyChanged   += OnAudioPropertyChanged;
        _audio.TrackEnded        += OnTrackEnded;
        _library.PropertyChanged += OnLibraryPropertyChanged;

        Volume    = _audio.Volume;
        Equalizer = new EqualizerViewModel(_audio);
        RefreshDisplayedTracks();
        StartPositionTimer();
    }

    // ── Sort ──────────────────────────────────────────────────────────────
    [RelayCommand]
    private void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending : ListSortDirection.Ascending;
        else
        {
            _sortColumn    = column;
            _sortDirection = ListSortDirection.Ascending;
        }
        UpdateSortIndicators();
        RefreshDisplayedTracks();
    }

    private void UpdateSortIndicators()
    {
        string ind = _sortDirection == ListSortDirection.Ascending ? "▲" : "▼";
        SortIndicatorTitle    = _sortColumn == "DisplayTitle"  ? ind : "";
        SortIndicatorArtist   = _sortColumn == "Artist"        ? ind : "";
        SortIndicatorAlbum    = _sortColumn == "Album"         ? ind : "";
        SortIndicatorGenre    = _sortColumn == "Genre"         ? ind : "";
        SortIndicatorYear     = _sortColumn == "Year"          ? ind : "";
        SortIndicatorTrackNum = _sortColumn == "TrackNumber"   ? ind : "";
        SortIndicatorDuration = _sortColumn == "Duration"      ? ind : "";
    }

    // ── Commands ──────────────────────────────────────────────────────────
    [RelayCommand]
    private void Play(Track? track = null)
    {
        if (track != null)
        {
            var idx = _playlist.Queue.IndexOf(track);
            if (idx < 0) { _playlist.Enqueue(track); idx = _playlist.Queue.Count - 1; }
            _playlist.PlayAt(idx);
            _audio.Play(track);
            CurrentTrack = track;
        }
        else if (_audio.State == PlaybackState.Playing)
        {
            // Play/pause button clicked while playing — pause
            _audio.Pause();
        }
        else if (_audio.State == PlaybackState.Paused)
        {
            _audio.Resume();
        }
        else if (_playlist.CurrentTrack != null)
        {
            _audio.Play(_playlist.CurrentTrack);
            CurrentTrack = _playlist.CurrentTrack;
        }
        else if (_playlist.Queue.Count > 0)
        {
            // Nothing selected — start from the top of the queue
            var first = _playlist.PlayAt(0)!;
            _audio.Play(first);
            CurrentTrack = first;
        }
    }

    [RelayCommand] private void Pause() => _audio.Pause();
    [RelayCommand] private void Stop()  => _audio.Stop();

    [RelayCommand]
    private void Next()
    {
        var track = _playlist.Next();
        if (track != null) Play(track);
    }

    [RelayCommand]
    private void Previous()
    {
        if (_audio.Position.TotalSeconds > 3) { _audio.Position = TimeSpan.Zero; return; }
        var track = _playlist.Previous();
        if (track != null) Play(track);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Music Folder" };
        if (dialog.ShowDialog() != true) return;

        StatusText = "Scanning…";
        IsScanning = true;

        var progress = new Progress<string>(f => StatusText = $"Scanning: {f}");
        await _library.ScanDirectoryAsync(dialog.FolderName, true, progress);

        RefreshDisplayedTracks();
        RefreshTreeItems();
        StatusText = $"Library: {_library.Tracks.Count:N0} tracks";
        IsScanning = false;
    }

    [RelayCommand]
    private void AddFilesToQueue()
    {
        foreach (Track t in DisplayedTracks)
            _playlist.Enqueue(t);
    }

    [RelayCommand] private void ClearQueue() => _playlist.Clear();
    [RelayCommand] private void RemoveFromQueue(Track track) => _playlist.Remove(track);

    [RelayCommand]
    private void SetView(string view)
    {
        CurrentView    = Enum.Parse<LibraryView>(view);
        SelectedArtist = null;
        SelectedAlbum  = null;
        SelectedGenre  = null;
        RefreshDisplayedTracks();
        RefreshTreeItems();
    }

    [RelayCommand]
    private void SelectTreeItem(string item)
    {
        System.Diagnostics.Debug.WriteLine($"[SelectTreeItem] item='{item}' CurrentView={CurrentView}");
        switch (CurrentView)
        {
            case LibraryView.Artists: SelectedArtist = item; break;
            case LibraryView.Albums:  SelectedAlbum  = item; break;
            case LibraryView.Genres:  SelectedGenre  = item; break;
        }
        RefreshDisplayedTracks();
    }

    partial void OnSearchQueryChanged(string value) => RefreshDisplayedTracks();
    partial void OnVolumeChanged(float value) => _audio.Volume = value;

    public void BeginScrub() => IsScrubbing = true;
    public void EndScrub(double value) { IsScrubbing = false; _audio.Position = TimeSpan.FromSeconds(value); }

    /// <summary>Seek triggered by the waveform scrubber control (fraction 0..1).</summary>
    public void SeekToFraction(double fraction)
    {
        var target = TimeSpan.FromSeconds(fraction * _audio.Duration.TotalSeconds);
        _audio.Position = target;
    }

    /// <summary>Kick off background waveform decode for the given file. Cancels any in-flight decode.</summary>
    public async void LoadWaveformAsync(string filePath)
    {
        _waveformCts?.Cancel();
        _waveformCts?.Dispose();
        _waveformCts = new CancellationTokenSource();
        WaveformData = null;

        try
        {
            WaveformData = await WaveformBuilder.BuildAsync(filePath, 1000, _waveformCts.Token);
        }
        catch (OperationCanceledException) { /* track changed before decode finished */ }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Waveform] Build error: {ex.Message}");
        }
    }

    // ── Library loaded callback ───────────────────────────────────────────
    public void OnLibraryLoaded()
    {
        RefreshDisplayedTracks();
        RefreshTreeItems();
        StatusText = $"Library: {_library.Tracks.Count:N0} tracks";
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private void RefreshDisplayedTracks()
    {
        // 1. Filter
        IEnumerable<Track> tracks = CurrentView switch
        {
            LibraryView.Artists when SelectedArtist != null => _library.GetTracksByArtist(SelectedArtist),
            LibraryView.Albums  when SelectedAlbum  != null => _library.GetTracksByAlbum(SelectedAlbum),
            LibraryView.Genres  when SelectedGenre  != null => _library.GetTracksByGenre(SelectedGenre),
            _ => string.IsNullOrWhiteSpace(SearchQuery)
                    ? (IEnumerable<Track>)_library.Tracks
                    : _library.Search(SearchQuery)
        };

        var list = tracks.ToList();
        System.Diagnostics.Debug.WriteLine(
            $"[RefreshDisplayedTracks] View={CurrentView} Artist={SelectedArtist} " +
            $"LibraryCount={_library.Tracks.Count} ResultCount={list.Count}");
        tracks = list;

        // 2. Sort via LINQ
        tracks = (_sortColumn, _sortDirection) switch
        {
            ("DisplayTitle",  ListSortDirection.Ascending)  => tracks.OrderBy(t => t.DisplayTitle),
            ("DisplayTitle",  ListSortDirection.Descending) => tracks.OrderByDescending(t => t.DisplayTitle),
            ("Artist",        ListSortDirection.Ascending)  => tracks.OrderBy(t => t.Artist),
            ("Artist",        ListSortDirection.Descending) => tracks.OrderByDescending(t => t.Artist),
            ("Album",         ListSortDirection.Ascending)  => tracks.OrderBy(t => t.Album),
            ("Album",         ListSortDirection.Descending) => tracks.OrderByDescending(t => t.Album),
            ("Genre",         ListSortDirection.Ascending)  => tracks.OrderBy(t => t.Genre),
            ("Genre",         ListSortDirection.Descending) => tracks.OrderByDescending(t => t.Genre),
            ("Year",          ListSortDirection.Ascending)  => tracks.OrderBy(t => t.Year),
            ("Year",          ListSortDirection.Descending) => tracks.OrderByDescending(t => t.Year),
            ("TrackNumber",   ListSortDirection.Ascending)  => tracks.OrderBy(t => t.TrackNumber),
            ("TrackNumber",   ListSortDirection.Descending) => tracks.OrderByDescending(t => t.TrackNumber),
            ("Duration",      ListSortDirection.Ascending)  => tracks.OrderBy(t => t.Duration),
            ("Duration",      ListSortDirection.Descending) => tracks.OrderByDescending(t => t.Duration),
            _ => tracks.OrderBy(t => t.DisplayTitle)
        };

        // 3. Mutate the existing collection in-place (same reference = binding stays live)
        DisplayedTracks.Clear();
        foreach (var t in list)
            DisplayedTracks.Add(t);
        OnPropertyChanged(nameof(DisplayedCount));
    }

    private void RefreshTreeItems()
    {
        TreeItems = CurrentView switch
        {
            LibraryView.Artists => new ObservableCollection<string>(_library.GetArtists()),
            LibraryView.Albums  => new ObservableCollection<string>(_library.GetAlbums()),
            LibraryView.Genres  => new ObservableCollection<string>(_library.GetGenres()),
            _ => []
        };
    }

    // ── Event handlers ────────────────────────────────────────────────────
    private void OnAudioPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (e.PropertyName == nameof(AudioService.State))
                IsPlaying = _audio.IsPlaying;
            if (e.PropertyName == nameof(AudioService.CurrentTrack))
            {
                CurrentTrack = _audio.CurrentTrack;
                if (CurrentTrack?.FilePath is string path)
                    LoadWaveformAsync(path);
                else
                    WaveformData = null;
            }
        });
    }

    private void OnTrackEnded(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[Crossfade] OnTrackEnded received — setting _crossfadeAdvancePending");
        _crossfadeAdvancePending = true;
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryService.IsScanning))
            IsScanning = _library.IsScanning;
    }

    // ── Position timer + auto-advance ─────────────────────────────────────
    private bool _advancePending;        // guard against double-firing
    private volatile bool _crossfadeAdvancePending; // set by crossfade timer thread, read by UI thread

    private void StartPositionTimer()
    {
        _positionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _positionTimer.Tick += (_, _) =>
        {
            // Crossfade timer fired on background thread — advance now on UI thread
            if (_crossfadeAdvancePending)
            {
                System.Diagnostics.Debug.WriteLine("[Crossfade] Position timer picked up _crossfadeAdvancePending — advancing");
                _crossfadeAdvancePending = false;
                if (!_advancePending)
                {
                    _advancePending = true;
                    AdvanceToNextTrack();
                }
                return;
            }

            if (_audio.IsPlaying)
            {
                if (!IsScrubbing)
                {
                    Duration         = _audio.Duration.TotalSeconds;
                    Position         = _audio.Position.TotalSeconds;
                    PositionText     = FormatTime(_audio.Position);
                    DurationText     = FormatTime(_audio.Duration);
                    WaveformProgress = Duration > 0 ? Position / Duration : 0;
                }

                // Non-crossfade auto-advance: position reached end
                if (!_advancePending && _audio.IsAtEnd)
                {
                    System.Diagnostics.Debug.WriteLine("[AutoAdvance] IsAtEnd triggered — advancing (no crossfade path)");
                    _advancePending = true;
                    AdvanceToNextTrack();
                }
            }
            else
            {
                _advancePending = false;
            }
        };
        _positionTimer.Start();
    }

    private void AdvanceToNextTrack()
    {
        var next = _playlist.Next();
        if (next != null)
        {
            _audio.Play(next);
            CurrentTrack    = next;
            _advancePending = false;
        }
        else
        {
            // End of queue — reset UI
            _audio.Stop();
            CurrentTrack     = null;
            IsPlaying        = false;
            Position         = 0;
            PositionText     = "0:00";
            DurationText     = "0:00";
            WaveformProgress = 0;
            WaveformData     = null;
            _advancePending  = false;
        }
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
