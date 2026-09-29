using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kosync.Database;

public class KosyncDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<SyncEvent> SyncEvents => Set<SyncEvent>();

    public KosyncDbContext(DbContextOptions<KosyncDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite loses DateTime.Kind on round-trip; every Timestamp we store is
        // already UTC, so restore that on read instead of it coming back Unspecified
        // (which would shift DateTimeOffset conversions by the local timezone offset).
        var utcTimestamp = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableUtcTimestamp = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<Document>()
            .HasOne(d => d.User)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Document>()
            .HasOne(d => d.Book)
            .WithMany(b => b.Documents)
            .HasForeignKey(d => d.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Device>()
            .HasOne(d => d.User)
            .WithMany(u => u.Devices)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Device>()
            .HasIndex(d => new { d.UserId, d.DeviceId })
            .IsUnique();

        modelBuilder.Entity<SyncEvent>()
            .HasOne(s => s.Document)
            .WithMany(d => d.SyncEvents)
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SyncEvent>()
            .HasOne(s => s.Device)
            .WithMany(d => d.SyncEvents)
            .HasForeignKey(s => s.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SyncEvent>()
            .Property(s => s.Timestamp)
            .HasConversion(utcTimestamp);

        modelBuilder.Entity<Document>()
            .Property(d => d.LastResetAt)
            .HasConversion(nullableUtcTimestamp);
    }
}
