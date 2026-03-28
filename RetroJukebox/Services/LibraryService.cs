using Microsoft.EntityFrameworkCore;
using RetroJukebox.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using TagLib;

namespace RetroJukebox.Services;

public class LibraryService : INotifyPropertyChanged
{
    private readonly BulkObservableCollection<Track> _tracks = new();
    private readonly string _dbPath;
    private bool _isScanning;
    private LibraryDbContext CreateContext() => new(_dbPath);

    public BulkObservableCollection<Track> Tracks => _tracks;
    public bool IsScanning { get => _isScanning; private set { _isScanning = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<Track>? TrackAdded;
    public event EventHandler? ScanCompleted;

    public LibraryService()
    {
        var appData = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RetroJukebox");
        System.IO.Directory.CreateDirectory(appData);
        _dbPath = System.IO.Path.Combine(appData, "library.db");

        try { EnsureDatabase(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] DB init failed: {ex}");
            _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "retrjukebox_fallback.db");
            try { EnsureDatabase(); } catch { /* give up on DB */ }
        }
        // Note: tracks are loaded separately via LoadLibraryAsync()
    }

    /// <summary>
    /// Load all tracks from SQLite into the in-memory ObservableCollection.
    /// Call this after the main window is shown so startup is non-blocking.
    /// </summary>
    public async Task LoadLibraryAsync()
    {
        IsScanning = true;
        try
        {
            // Step 1: Read ALL data from SQLite entirely on background thread
            // EF Core DbContext is NOT thread-safe — never share it across threads
            List<Track> allTracks = await Task.Run(() =>
            {
                using var ctx = CreateContext();
                // Materialize fully before leaving Task.Run — no lazy loading after this point
                return ctx.Tracks
                    .AsNoTracking()
                    .ToList()                           // pull everything into memory first
                    .Where(e => System.IO.File.Exists(e.FilePath))
                    .OrderBy(e => e.Artist)
                    .ThenBy(e => e.Album)
                    .ThenBy(e => e.TrackNumber)
                    .Select(e => e.ToTrack())
                    .ToList();                          // fully materialised List<Track>
            });

            // Step 2: Add all tracks in one bulk operation — single Reset notification
            _tracks.AddRange(allTracks);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] LoadLibraryAsync failed: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    // ── Database init ─────────────────────────────────────────────────────
    private void EnsureDatabase()
    {
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }



    // ── Directory scanning ────────────────────────────────────────────────
    public async Task ScanDirectoryAsync(string path, bool recursive = true, IProgress<string>? progress = null)
    {
        IsScanning = true;
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var files = System.IO.Directory.GetFiles(path, "*.*", searchOption)
            .Where(f => Track.IsSupported(f))
            .ToList();

        // Build set of already-known paths for fast lookup
        var knownPaths = new HashSet<string>(_tracks.Select(t => t.FilePath), StringComparer.OrdinalIgnoreCase);

        // Read metadata + write to DB entirely on background thread
        // Then push results to UI in batches on the UI thread
        var newTracks = await Task.Run(() =>
        {
            var results   = new List<Track>();
            var dbBatch   = new List<TrackEntity>(500);
            int processed = 0;

            // Each Task.Run gets its own DbContext — never share across threads
            using var ctx = CreateContext();

            foreach (var file in files)
            {
                if (knownPaths.Contains(file)) continue;

                var track  = ReadMetadata(file);
                var entity = TrackEntity.FromTrack(track);

                results.Add(track);
                dbBatch.Add(entity);
                knownPaths.Add(file);
                processed++;

                progress?.Report($"{processed:N0} / {files.Count:N0} scanned");

                if (dbBatch.Count >= 500)
                {
                    FlushBatch(ctx, dbBatch);
                    dbBatch.Clear();
                }
            }

            if (dbBatch.Count > 0) FlushBatch(ctx, dbBatch);
            return results;
        });

        // Add all new tracks in one bulk operation, then fire ScanCompleted once
        _tracks.AddRange(newTracks);
        foreach (var t in newTracks) TrackAdded?.Invoke(this, t);

        IsScanning = false;
        ScanCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static void FlushBatch(LibraryDbContext ctx, List<TrackEntity> batch)
    {
        try
        {
            ctx.Tracks.AddRange(batch);
            ctx.SaveChanges();
            // Detach to keep memory clean during large scans
            foreach (var e in batch)
                ctx.Entry(e).State = EntityState.Detached;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] Batch flush error: {ex.Message}");
        }
    }

    // ── Metadata reading (TagLib) ─────────────────────────────────────────
    public Track ReadMetadata(string filePath)
    {
        var track = new Track
        {
            FilePath = filePath,
            Title    = System.IO.Path.GetFileNameWithoutExtension(filePath)
        };

        try
        {
            // Use PictureLazy to avoid crashing on corrupt embedded art frames
            using var tagFile = TagLib.File.Create(filePath,
                TagLib.ReadStyle.Average | TagLib.ReadStyle.PictureLazy);

            track.Title       = tagFile.Tag.Title ?? System.IO.Path.GetFileNameWithoutExtension(filePath);
            track.Artist      = tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist ?? "Unknown Artist";
            track.Album       = tagFile.Tag.Album ?? "Unknown Album";
            track.Genre       = tagFile.Tag.FirstGenre ?? "Unknown";
            track.Year        = (int)tagFile.Tag.Year;
            track.TrackNumber = (int)tagFile.Tag.Track;
            track.Duration    = tagFile.Properties.Duration;

            // Safely attempt to read art separately
            try
            {
                if (tagFile.Tag.Pictures?.Length > 0)
                    track.AlbumArt = tagFile.Tag.Pictures[0].Data?.Data;
            }
            catch
            {
                // Corrupt picture frame — skip art, keep track
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] Metadata error for {filePath}: {ex.Message}");
            // Still return track with filename as title so it appears in library
        }

        return track;
    }

    // ── Metadata writing ──────────────────────────────────────────────────
    public bool SaveMetadata(Track track)
    {
        // Transient tracks are not in the library — skip DB write
        if (track.IsTransient) return false;
        try
        {
            using var tagFile = TagLib.File.Create(track.FilePath);
            tagFile.Tag.Title      = track.Title;
            tagFile.Tag.Performers = [track.Artist];
            tagFile.Tag.Album      = track.Album;
            tagFile.Tag.Genres     = [track.Genre];
            tagFile.Tag.Year       = (uint)track.Year;
            tagFile.Tag.Track      = (uint)track.TrackNumber;

            if (track.AlbumArt != null)
            {
                tagFile.Tag.Pictures =
                [
                    new Picture(new ByteVector(track.AlbumArt))
                    {
                        Type     = PictureType.FrontCover,
                        MimeType = "image/jpeg"
                    }
                ];
            }

            tagFile.Save();

            // Update SQLite
            using var ctx = CreateContext();
            var entity = ctx.Tracks.FirstOrDefault(t => t.FilePath == track.FilePath);
            if (entity != null)
            {
                entity.Title       = track.Title;
                entity.Artist      = track.Artist;
                entity.Album       = track.Album;
                entity.Genre       = track.Genre;
                entity.Year        = track.Year;
                entity.TrackNumber = track.TrackNumber;
                entity.AlbumArt    = track.AlbumArt;
                ctx.SaveChanges();
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] Save metadata error: {ex.Message}");
            return false;
        }
    }

    // ── Queries (run on in-memory collection for speed) ───────────────────
    public IEnumerable<string> GetArtists() =>
        _tracks.Select(t => t.Artist).Where(a => !string.IsNullOrWhiteSpace(a))
               .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a);

    public IEnumerable<string> GetAlbums(string? artist = null) =>
        _tracks.Where(t => artist == null || string.Equals(t.Artist, artist, StringComparison.OrdinalIgnoreCase))
               .Select(t => t.Album).Where(a => !string.IsNullOrWhiteSpace(a))
               .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a);

    public IEnumerable<string> GetGenres() =>
        _tracks.Select(t => t.Genre).Where(g => !string.IsNullOrWhiteSpace(g))
               .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g);

    public IEnumerable<Track> GetTracksByArtist(string artist) =>
        _tracks.Where(t => string.Equals(t.Artist, artist, StringComparison.OrdinalIgnoreCase))
               .OrderBy(t => t.Album).ThenBy(t => t.TrackNumber);

    public IEnumerable<Track> GetTracksByAlbum(string album) =>
        _tracks.Where(t => string.Equals(t.Album, album, StringComparison.OrdinalIgnoreCase))
               .OrderBy(t => t.TrackNumber);

    public IEnumerable<Track> GetTracksByGenre(string genre) =>
        _tracks.Where(t => string.Equals(t.Genre, genre, StringComparison.OrdinalIgnoreCase))
               .OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.TrackNumber);

    public IEnumerable<Track> Search(string query) =>
        string.IsNullOrWhiteSpace(query)
            ? _tracks
            : _tracks.Where(t =>
                t.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Album.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Genre.Contains(query, StringComparison.OrdinalIgnoreCase));

    public void RemoveTrack(Track track)
    {
        _tracks.Remove(track);
        try
        {
            using var ctx = CreateContext();
            var entity = ctx.Tracks.FirstOrDefault(t => t.FilePath == track.FilePath);
            if (entity != null) { ctx.Tracks.Remove(entity); ctx.SaveChanges(); }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] Remove error: {ex.Message}");
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
