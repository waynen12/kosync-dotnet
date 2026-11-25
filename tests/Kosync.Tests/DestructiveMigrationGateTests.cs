using Kosync.Database;
using Kosync.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kosync.Tests;

// ADR 0009: a Destructive Migration never runs unattended. The end-to-end
// cases boot KosyncDbInitializer.Initialize directly against a real file -
// not the `:memory:` connection the rest of the suite shares - because the
// gate's skip rule is keyed on exactly that distinction (issue #49).
public class DestructiveMigrationGateTests : IDisposable
{
    // The newest migration today, and its immediate predecessor. Stopping a
    // database here leaves exactly one migration pending - AddDocumentHash-
    // UniqueIndex itself - rather than it and everything destructive after
    // it, which is every other point in the chain: the newest migration
    // happens to be Destructive, so any earlier stop point leaves it
    // pending too and cannot isolate one case from another.
    private const string MigrationBeforeTheNewest = "AddProgressResets";

    private const string TheNewestMigration = "20261105204525_AddDocumentHashUniqueIndex";

    private const string AddBookUserId = "20261020194356_AddBookUserId";

    private readonly string _dataDirectory;
    private readonly string _databasePath;
    private readonly EnvironmentVariableScope _noAmbientAllowList;

    public DestructiveMigrationGateTests()
    {
        _dataDirectory = Directory.CreateTempSubdirectory("kosync-gate-tests-").FullName;
        _databasePath = Path.Combine(_dataDirectory, Constants.Storage.DatabaseFileName);

        // Read straight from the process environment, so an ambient value
        // in the shell or on CI cannot decide what these tests boot with.
        _noAmbientAllowList =
            new EnvironmentVariableScope(Constants.EnvironmentVariables.AllowDestructiveMigrations, null);
    }

    [Fact]
    public void FreshDatabase_MigratesEverythingWithoutGating()
    {
        // No file exists yet - every migration, including every Destructive
        // one, is trivially "pending" against it. The gate must not fire on
        // the one case ADR 0009 says a deploy never has to think about.
        Boot();

        using var db = ReadEverything();
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    [Fact]
    public void ExistingDatabase_WithAnUnpermittedDestructiveMigrationPending_RefusesToStart()
    {
        StopExistingDatabaseAt(MigrationBeforeTheNewest);

        var exception = Assert.Throws<InvalidOperationException>(Boot);

        Assert.Contains(TheNewestMigration, exception.Message);
        Assert.Contains(Constants.EnvironmentVariables.AllowDestructiveMigrations, exception.Message);

        // Refused before Migrate() ran, not merely logged alongside it.
        using var db = ReadEverything();
        Assert.Contains(TheNewestMigration, db.Database.GetPendingMigrations());
    }

    [Fact]
    public void ExistingDatabase_WithThatMigrationNamedInTheAllowList_Proceeds()
    {
        StopExistingDatabaseAt(MigrationBeforeTheNewest);

        using var _ = new EnvironmentVariableScope(
            Constants.EnvironmentVariables.AllowDestructiveMigrations, TheNewestMigration);

        Boot();

        using var db = ReadEverything();
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    // The override names ids, not a blanket "yes" - naming a different
    // pending Destructive Migration does not also permit this one.
    [Fact]
    public void ExistingDatabase_WithADifferentMigrationNamedInTheAllowList_StillRefuses()
    {
        StopExistingDatabaseAt(MigrationBeforeTheNewest);

        using var _ = new EnvironmentVariableScope(
            Constants.EnvironmentVariables.AllowDestructiveMigrations, AddBookUserId);

        Assert.Throws<InvalidOperationException>(Boot);
    }

    // A pending migration that is not on the Destructive list at all must
    // not gate - tested directly against the decision function rather than
    // through a real file, because in today's chain the newest migration is
    // always Destructive and so is always pending alongside whatever
    // non-destructive migration a stopped-database test would try to
    // isolate.
    [Fact]
    public void UnattendedDestructiveMigrations_IgnoresAPendingMigrationNotOnTheList()
    {
        var unattended = DestructiveMigrationGate.UnattendedDestructiveMigrations(
            ["20261025190907_AddServerSettings"]);

        Assert.Empty(unattended);
    }

    [Fact]
    public void UnattendedDestructiveMigrations_ReturnsAPendingDestructiveMigrationNotPermitted()
    {
        var unattended = DestructiveMigrationGate.UnattendedDestructiveMigrations([AddBookUserId]);

        Assert.Equal([AddBookUserId], unattended);
    }

    [Fact]
    public void UnattendedDestructiveMigrations_ExcludesOneNamedInTheAllowList()
    {
        using var _ = new EnvironmentVariableScope(
            Constants.EnvironmentVariables.AllowDestructiveMigrations, AddBookUserId);

        var unattended = DestructiveMigrationGate.UnattendedDestructiveMigrations([AddBookUserId]);

        Assert.Empty(unattended);
    }

    private void StopExistingDatabaseAt(string migrationName)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        connection.MigrateTo(migrationName);
    }

    private void Boot()
    {
        var tenant = new TenantContext();
        using var db = new KosyncDbContext(Options, tenant);

        KosyncDbInitializer.Initialize(db, tenant, _dataDirectory, TestPasswords.CreateHasher(), NullLogger.Instance);
    }

    private KosyncDbContext ReadEverything() => TestDb.CreateSystemContext(Options);

    private DbContextOptions<KosyncDbContext> Options =>
        new DbContextOptionsBuilder<KosyncDbContext>().UseSqlite($"Data Source={_databasePath}").Options;

    public void Dispose()
    {
        _noAmbientAllowList.Dispose();

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
