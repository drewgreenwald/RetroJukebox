using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RetroJukebox.Models;

public class Track : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private string _genre = string.Empty;
    private int _year;
    private int _trackNumber;
    private TimeSpan _duration;
    private byte[]? _albumArt;
    private bool _isPlaying;
    private bool _isSelected;

    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Artist
    {
        get => _artist;
        set { _artist = value; OnPropertyChanged(); }
    }

    public string Album
    {
        get => _album;
        set { _album = value; OnPropertyChanged(); }
    }

    public string Genre
    {
        get => _genre;
        set { _genre = value; OnPropertyChanged(); }
    }

    public int Year
    {
        get => _year;
        set { _year = value; OnPropertyChanged(); }
    }

    public int TrackNumber
    {
        get => _trackNumber;
        set { _trackNumber = value; OnPropertyChanged(); }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationString)); }
    }

    public byte[]? AlbumArt
    {
        get => _albumArt;
        set { _albumArt = value; OnPropertyChanged(); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// True for tracks opened directly from disk (not added to the library).
    /// Transient tracks are never written to SQLite and display a file icon in the queue.
    /// </summary>
    public bool IsTransient { get; set; }

    public string DurationString => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? System.IO.Path.GetFileNameWithoutExtension(FilePath)
        : Title;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static readonly string[] SupportedExtensions =
        { ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".mp4" };

    public static bool IsSupported(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return Array.Exists(SupportedExtensions, e => e == ext);
    }
}
