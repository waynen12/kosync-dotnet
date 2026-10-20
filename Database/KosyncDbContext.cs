using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kosync.Database;

public class KosyncDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<SyncEvent> SyncEvents => Set<SyncEvent>();

    private readonly TenantContext _tenant;

    public KosyncDbContext(DbContextOptions<KosyncDbContext> options, TenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    // The global query filters below close over these rather than over
    // _tenant directly: EF Core compiles the model once and then re-evaluates
    // any part of a filter that does not involve the entity as a query
    // parameter on every execution. That is what lets the tenant be set per
    // operation against a model built at startup.
    private int FilterUserId => _tenant.FilterUserId;

    // Public so that the three system opt-outs ADR 0004 allows can be
    // asserted on rather than taken on trust. Read-only: turning the filters
    // off still means calling TenantContext.BypassFilters with a reason.
    public bool TenantFiltersBypassed => _tenant.IsBypassed;

    // Every relationship in this model crosses from a filtered principal to a
    // filtered dependent and both filter on the same User, so a filtered-out
    // principal never leaves a visible dependent dangling. EF cannot see that
    // and warns about each required navigation into a filtered entity; the
    // warning would fire on every query.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
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

        // Deleting a User takes their Books with them. Without this, removing
        // a User would trip the foreign key: ManagementController sweeps up
        // their now-orphaned Books only *after* the User row is gone.
        modelBuilder.Entity<Book>()
            .HasOne(b => b.User)
            .WithMany(u => u.Books)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

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

        ApplyTenantFilters(modelBuilder);
    }

    // Tenant scoping, the whole of it (ADR 0004). Enforced here rather than by
    // threading a userId parameter through every service method, so queries
    // are scoped by default and a query added later cannot silently forget.
    // It also authorises mutations for free: a filtered lookup of another
    // User's Book returns null, and the existing "not found" paths handle it.
    //
    // Users are deliberately absent. /login must find a User by name before
    // it knows who is logging in, and managing accounts is an Administrator
    // capability that does not extend to reading anybody's library.
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>()
            .HasQueryFilter(b => TenantFiltersBypassed || b.UserId == FilterUserId);

        modelBuilder.Entity<Document>()
            .HasQueryFilter(d => TenantFiltersBypassed || d.UserId == FilterUserId);

        modelBuilder.Entity<Device>()
            .HasQueryFilter(d => TenantFiltersBypassed || d.UserId == FilterUserId);

        // A SyncEvent has no UserId of its own and is only ever reached
        // through a Document or a Device, both of which are already scoped.
        // Filtering it anyway is what makes a future query straight off
        // DbSet<SyncEvent> safe rather than merely unwritten.
        modelBuilder.Entity<SyncEvent>()
            .HasQueryFilter(s => TenantFiltersBypassed || s.Document.UserId == FilterUserId);
    }
}
