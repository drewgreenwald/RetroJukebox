using RetroJukebox.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace RetroJukebox.Services;

/// <summary>
/// A named, persisted playlist stored as a JSON file in
/// %AppData%\RetroJukebox\Playlists\{name}.json
/// </summary>
public class SavedPlaylist
{
    public string Name        { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<string> FilePaths { get; set; } = [];

    /// <summary>Number of tracks in this playlist.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public int TrackCount => FilePaths.Count;
}

/// <summary>
/// Manages the collection of user-saved playlists.
/// Each playlist is a <c>.json</c> file under
/// <c>%AppData%\RetroJukebox\Playlists\</c>.
/// </summary>
public class SavedPlaylistService : INotifyPropertyChanged
{
    private readonly string _playlistsDir;

    public ObservableCollection<SavedPlaylist> Playlists { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public SavedPlaylistService()
    {
        _playlistsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RetroJukebox", "Playlists");
        Directory.CreateDirectory(_playlistsDir);
        LoadAll();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Save the current queue as a named playlist.
    /// If a playlist with the same name already exists it is overwritten.
    /// </summary>
    public bool Save(string name, IEnumerable<Track> tracks)
    {
        name = SanitizeName(name);
        if (string.IsNullOrWhiteSpace(name)) return false;

        var pl = new SavedPlaylist
        {
            Name      = name,
            CreatedAt = DateTime.Now,
            FilePaths = tracks.Select(t => t.FilePath).ToList()
        };

        try
        {
            WritePlaylist(pl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SavedPlaylistService] Save error: {ex.Message}");
            return false;
        }

        // Update or insert in the observable collection
        var existing = Playlists.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var idx = Playlists.IndexOf(existing);
            Playlists[idx] = pl;
        }
        else
        {
            // Insert sorted by name
            var insertAt = Playlists.TakeWhile(p => StringComparer.OrdinalIgnoreCase.Compare(p.Name, name) < 0).Count();
            Playlists.Insert(insertAt, pl);
        }

        return true;
    }

    /// <summary>
    /// Delete a saved playlist by name.
    /// </summary>
    public bool Delete(SavedPlaylist playlist)
    {
        var path = PlaylistPath(playlist.Name);
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SavedPlaylistService] Delete error: {ex.Message}");
            return false;
        }

        Playlists.Remove(playlist);
        return true;
    }

    /// <summary>
    /// Resolve a saved playlist's file paths back to Track objects,
    /// falling back to metadata-only reads for files not in the library.
    /// </summary>
    public List<Track> Resolve(SavedPlaylist playlist)
    {
        var result = new List<Track>();
        foreach (var path in playlist.FilePaths)
        {
            if (!File.Exists(path)) continue;
            var track = App.LibraryService.Tracks.FirstOrDefault(t =>
                string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase))
                ?? App.LibraryService.ReadMetadata(path);
            result.Add(track);
        }
        return result;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void LoadAll()
    {
        Playlists.Clear();
        var files = Directory.GetFiles(_playlistsDir, "*.json")
                             .OrderBy(f => Path.GetFileNameWithoutExtension(f),
                                      StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var pl   = Newtonsoft.Json.JsonConvert.DeserializeObject<SavedPlaylist>(json);
                if (pl is not null) Playlists.Add(pl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SavedPlaylistService] Load error ({file}): {ex.Message}");
            }
        }
    }

    private void WritePlaylist(SavedPlaylist pl)
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(pl, Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(PlaylistPath(pl.Name), json);
    }

    private string PlaylistPath(string name) =>
        Path.Combine(_playlistsDir, $"{SanitizeName(name)}.json");

    /// <summary>Strip characters that are illegal in Windows file names.</summary>
    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray());
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
