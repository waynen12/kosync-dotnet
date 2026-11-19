using Kosync.Tests.TestSupport;
using Microsoft.Data.Sqlite;

namespace Kosync.Tests;

// ADR 0009: a migration never applies without a backup first. These start
// the real app (KosyncWebApplicationFactory) against a file-backed database
// via DataDirectory, because a backup test without a real file on disk is
// meaningless - an in-memory database has no write-ahead log to lose.
public class DatabaseBackupTests
{
    // The migration immediately before the latest in the assembly, so a
    // database stopped here has exactly one pending migration once the app
    // starts: the one-migration-behind case the acceptance criteria name.
    private const string SchemaOneMigrationBehindLatest = "AddProgressResets";

    [Fact]
    public void Startup_OneMigrationBehind_BacksUpUncheckpointedWalContentBeforeMigrating()
    {
        using var allowDestructive = new EnvironmentVariableScope(
            Constants.EnvironmentVariables.AllowDestructiveMigrations,
            string.Join(",", Constants.Migrations.Destructive));

        using var factory = new KosyncWebApplicationFactory { FileBacked = true };
        Directory.CreateDirectory(factory.DataDirectory);

        var dbPath = Path.Combine(factory.DataDirectory, Constants.Storage.DatabaseFileName);

        using var writer = MigrationTestDatabase.OpenFileBackedAt(dbPath, SchemaOneMigrationBehindLatest);

        writer.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator", "PasswordState") VALUES ('reader', 'x', 1, 0, 1);
            """);

        // A second connection to the same file, so closing `writer` next
        // does not drop the open-connection count to zero - the condition
        // under which SQLite checkpoints the write-ahead log automatically
        // and would erase the very content this test exists to prove
        // survives a backup.
        using var keepAlive = MigrationTestDatabase.KeepFileAliveAt(dbPath);
        writer.Close();

        Assert.True(new FileInfo(dbPath + "-wal").Length > 0);

        using var client = factory.CreateClient();

        keepAlive.Close();

        var backupsDirectory = Path.Combine(factory.DataDirectory, Constants.Storage.BackupsDirectoryName);
        var backupFile = Assert.Single(Directory.GetFiles(backupsDirectory));

        using var backupConnection = new SqliteConnection($"Data Source={backupFile}");
        backupConnection.Open();

        Assert.Equal("reader", backupConnection.SingleValueOf("""SELECT "Username" FROM "Users" WHERE "Username" = 'reader'"""));
    }

    [Fact]
    public void Startup_AgainstAnInMemoryDatabase_WritesNoBackup()
    {
        using var factory = new KosyncWebApplicationFactory();

        using var client = factory.CreateClient();

        Assert.False(Directory.Exists(Path.Combine(factory.DataDirectory, Constants.Storage.BackupsDirectoryName)));
    }

    [Fact]
    public void Startup_WithNoPendingMigrations_WritesNoBackup()
    {
        using var factory = new KosyncWebApplicationFactory { FileBacked = true };
        Directory.CreateDirectory(factory.DataDirectory);

        var dbPath = Path.Combine(factory.DataDirectory, Constants.Storage.DatabaseFileName);

        using (var writer = MigrationTestDatabase.OpenFileBackedAt(dbPath, SchemaOneMigrationBehindLatest))
        {
            writer.MigrateToLatest();
        }

        using var client = factory.CreateClient();

        Assert.False(Directory.Exists(Path.Combine(factory.DataDirectory, Constants.Storage.BackupsDirectoryName)));
    }

    [Fact]
    public void Startup_WhenTheBackupTargetIsUnwritable_Throws()
    {
        using var factory = new KosyncWebApplicationFactory { FileBacked = true };
        Directory.CreateDirectory(factory.DataDirectory);

        var dbPath = Path.Combine(factory.DataDirectory, Constants.Storage.DatabaseFileName);

        using (var writer = MigrationTestDatabase.OpenFileBackedAt(dbPath, SchemaOneMigrationBehindLatest))
        {
        }

        // A plain file sitting where the backups directory needs to go:
        // Directory.CreateDirectory throws on it, the same failure mode as
        // a backups mount the process cannot write to.
        File.WriteAllText(
            Path.Combine(factory.DataDirectory, Constants.Storage.BackupsDirectoryName), "not a directory");

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
