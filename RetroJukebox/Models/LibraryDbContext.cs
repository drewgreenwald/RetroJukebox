using Microsoft.EntityFrameworkCore;

namespace RetroJukebox.Models;

public class LibraryDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<TrackEntity> Tracks => Set<TrackEntity>();

    public LibraryDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackEntity>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.FilePath).IsRequired();
            e.Property(t => t.Title).HasDefaultValue("");
            e.Property(t => t.Artist).HasDefaultValue("");
            e.Property(t => t.Album).HasDefaultValue("");
            e.Property(t => t.Genre).HasDefaultValue("");
        });
    }
}
