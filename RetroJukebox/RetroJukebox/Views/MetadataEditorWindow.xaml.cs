using Microsoft.Win32;
using RetroJukebox.Models;
using RetroJukebox.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace RetroJukebox.Views;

public partial class MetadataEditorWindow : Window
{
    private readonly Track _track;
    private readonly MusicBrainzService _mb = new();
    private byte[]? _pendingArt;
    private bool _artChanged;

    public MetadataEditorWindow(Track track)
    {
        InitializeComponent();
        _track = track;
        PopulateFields(track);

        // Pre-fill search boxes
        SearchTitleBox.Text  = track.DisplayTitle;
        SearchArtistBox.Text = track.Artist;
    }

    // ── Field population ──────────────────────────────────────────────────
    private void PopulateFields(Track t)
    {
        TitleBox.Text    = t.Title;
        ArtistBox.Text   = t.Artist;
        AlbumBox.Text    = t.Album;
        GenreBox.Text    = t.Genre;
        YearBox.Text     = t.Year > 0 ? t.Year.ToString() : "";
        TrackNumBox.Text = t.TrackNumber > 0 ? t.TrackNumber.ToString() : "";
        FilePathBox.Text = t.FilePath;
        ShowArt(t.AlbumArt);
    }

    private void ShowArt(byte[]? artBytes)
    {
        if (artBytes == null || artBytes.Length == 0)
        {
            ArtImage.Source = null;
            return;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(artBytes);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            ArtImage.Source = bmp;
        }
        catch { ArtImage.Source = null; }
    }

    // ── Online fetch ──────────────────────────────────────────────────────
    private async void FetchOnline(object sender, RoutedEventArgs e)
    {
        var title  = SearchTitleBox.Text.Trim();
        var artist = SearchArtistBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
        {
            StatusText.Text = "Enter a title or artist to search.";
            return;
        }

        SetBusy(true, "Searching MusicBrainz…");
        ResultsList.ItemsSource = null;
        ResultsPlaceholder.Visibility = Visibility.Collapsed;
        ApplyResultBtn.IsEnabled = false;

        var (results, error) = await _mb.SearchAsync(title, artist, maxResults: 12);

        SetBusy(false);

        if (error != null)
        {
            StatusText.Text = $"Search failed: {error}";
            ResultsPlaceholder.Text = error;
            ResultsPlaceholder.Visibility = Visibility.Visible;
        }
        else if (results.Count == 0)
        {
            StatusText.Text = "No results found. Try adjusting the search terms.";
            ResultsPlaceholder.Text = "No results found.";
            ResultsPlaceholder.Visibility = Visibility.Visible;
        }
        else
        {
            ResultsList.ItemsSource = results;
            ResultsPlaceholder.Visibility = Visibility.Collapsed;
            StatusText.Text = $"{results.Count} result{(results.Count == 1 ? "" : "s")} found — click one to preview, then Apply.";
        }
    }

    // ── Result selection — preview in fields ──────────────────────────────
    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not MusicBrainzResult result) return;
        ApplyResultBtn.IsEnabled = true;

        // Preview the selected result in the edit fields
        TitleBox.Text  = result.Title;
        ArtistBox.Text = result.Artist;
        AlbumBox.Text  = result.Album;
        YearBox.Text   = result.Year > 0 ? result.Year.ToString() : "";
        if (result.TrackNumber > 0)
            TrackNumBox.Text = result.TrackNumber.ToString();
    }

    // ── Apply — also fetch album art ──────────────────────────────────────
    private async void ApplyResult(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not MusicBrainzResult result) return;

        SetBusy(true, "Fetching album art…");

        try
        {
            if (!string.IsNullOrEmpty(result.ReleaseId))
            {
                // Fetch art and genre in parallel
                var artTask   = _mb.GetCoverArtAsync(result.ReleaseId);
                var genreTask = _mb.GetGenreAsync(result.ReleaseId);
                await Task.WhenAll(artTask, genreTask);

                var (art, artError) = artTask.Result;
                if (art != null && art.Length > 0)
                {
                    _pendingArt = art;
                    _artChanged = true;
                    ShowArt(art);
                    StatusText.Text = "Metadata + album art applied. Click 'Save to File' to write to disk.";
                }
                else
                {
                    StatusText.Text = $"Metadata applied. {artError ?? "No album art found."}";
                }

                var genre = genreTask.Result;
                if (!string.IsNullOrEmpty(genre) &&
                    (string.IsNullOrWhiteSpace(GenreBox.Text) || GenreBox.Text == "Unknown"))
                    GenreBox.Text = genre;
            }
            else
            {
                StatusText.Text = "Metadata applied. No release ID — art unavailable.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Art fetch failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── Save to file ──────────────────────────────────────────────────────
    private void Save(object sender, RoutedEventArgs e)
    {
        _track.Title       = TitleBox.Text.Trim();
        _track.Artist      = ArtistBox.Text.Trim();
        _track.Album       = AlbumBox.Text.Trim();
        _track.Genre       = GenreBox.Text.Trim();
        _track.Year        = int.TryParse(YearBox.Text, out var y) ? y : 0;
        _track.TrackNumber = int.TryParse(TrackNumBox.Text, out var tn) ? tn : 0;

        if (_artChanged && _pendingArt != null)
            _track.AlbumArt = _pendingArt;

        bool ok = App.LibraryService.SaveMetadata(_track);

        if (ok)
        {
            StatusText.Text = "Saved successfully.";
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.Text = "Could not write tags — file may be read-only.";
        }
    }

    // ── Local art ─────────────────────────────────────────────────────────
    private void LoadLocalArt(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select Album Art",
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _pendingArt = File.ReadAllBytes(dlg.FileName);
            _artChanged = true;
            ShowArt(_pendingArt);
            StatusText.Text = "Image loaded. Click 'Save to File' to embed it.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load image: {ex.Message}";
        }
    }

    private void ClearArt(object sender, RoutedEventArgs e)
    {
        _pendingArt     = null;
        _artChanged     = true;
        ArtImage.Source = null;
        StatusText.Text = "Album art cleared. Click 'Save to File' to apply.";
    }

    private void Cancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ── UI helpers ────────────────────────────────────────────────────────
    private void SetBusy(bool busy, string status = "")
    {
        FetchProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrEmpty(status)) StatusText.Text = status;
        // Don't disable IsEnabled on the whole window — breaks async continuations
        // Just disable the search button to prevent double-clicks
    }
}
