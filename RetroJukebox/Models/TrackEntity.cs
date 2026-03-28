using Microsoft.EntityFrameworkCore;

namespace RetroJukebox.Models;

/// <summary>EF Core entity — mirrors Track but without UI notification overhead.</summary>
[Index(nameof(Artist)), Index(nameof(Album)), Index(nameof(Genre)), Index(nameof(FilePath), IsUnique = true)]
public class TrackEntity
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public int Year { get; set; }
    public int TrackNumber { get; set; }
    public double DurationSeconds { get; set; }
    public byte[]? AlbumArt { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    public Track ToTrack() => new()
    {
        Id          = Id,
        FilePath    = FilePath,
        Title       = Title,
        Artist      = Artist,
        Album       = Album,
        Genre       = Genre,
        Year        = Year,
        TrackNumber = TrackNumber,
        Duration    = TimeSpan.FromSeconds(DurationSeconds),
        AlbumArt    = AlbumArt
    };

    public static TrackEntity FromTrack(Track t) => new()
    {
        Id              = t.Id,
        FilePath        = t.FilePath,
        Title           = t.DisplayTitle,
        Artist          = t.Artist,
        Album           = t.Album,
        Genre           = t.Genre,
        Year            = t.Year,
        TrackNumber     = t.TrackNumber,
        DurationSeconds = t.Duration.TotalSeconds,
        AlbumArt        = t.AlbumArt
    };
}
