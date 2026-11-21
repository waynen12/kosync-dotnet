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

    // Issue #50: unbounded backups move the failure ADR 0009 fixed from data
    // loss to disk exhaustion. Pre-seeding backups dated in the past rather
    // than running the backup path repeatedly in real time, because two runs
    // close enough together would land on the same to-the-second timestamp
    // and collide on the same filename - these prove the same retention rule
    // without depending on the clock.
    [Fact]
    public void Startup_WithMoreBackupsThanTheRetentionLimit_PrunesToTheNewestN()
    {
        using var allowDestructive = new EnvironmentVariableScope(
            Constants.EnvironmentVariables.AllowDestructiveMigrations,
            string.Join(",", Constants.Migrations.Destructive));

        using var factory = new KosyncWebApplicationFactory { FileBacked = true, MaxBackupsRetained = 2 };
        Directory.CreateDirectory(factory.DataDirectory);

        var dbPath = Path.Combine(factory.DataDirectory, Constants.Storage.DatabaseFileName);
        using (var writer = MigrationTestDatabase.OpenFileBackedAt(dbPath, SchemaOneMigrationBehindLatest))
        {
        }

        var backupsDirectory = Path.Combine(factory.DataDirectory, Constants.Storage.BackupsDirectoryName);

        // Three backups as if from three earlier deploys, already one more
        // than the limit of two before this run's own backup is added.
        var oldest = WriteSyntheticBackup(backupsDirectory, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Old1");
        var middle = WriteSyntheticBackup(backupsDirectory, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), "Old2");
        var newest = WriteSyntheticBackup(backupsDirectory, new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc), "Old3");

        using var client = factory.CreateClient();

        var remaining = Directory.GetFiles(backupsDirectory);
        Assert.Equal(2, remaining.Length);
        Assert.DoesNotContain(oldest, remaining);
        Assert.DoesNotContain(middle, remaining);
        Assert.Contains(newest, remaining);
    }

    [Fact]
    public void Startup_WithAForeignFileInTheBackupsDirectory_LeavesItAlone()
    {
        using var allowDestructive = new EnvironmentVariableScope(
            Constants.EnvironmentVariables.AllowDestructiveMigrations,
            string.Join(",", Constants.Migrations.Destructive));

        using var factory = new KosyncWebApplicationFactory { FileBacked = true, MaxBackupsRetained = 1 };
        Directory.CreateDirectory(factory.DataDirectory);

        var dbPath = Path.Combine(factory.DataDirectory, Constants.Storage.DatabaseFileName);
        using (var writer = MigrationTestDatabase.OpenFileBackedAt(dbPath, SchemaOneMigrationBehindLatest))
        {
        }

        var backupsDirectory = Path.Combine(factory.DataDirectory, Constants.Storage.BackupsDirectoryName);
        Directory.CreateDirectory(backupsDirectory);

        // Not named the way this mechanism names its own files, so pruning
        // must never treat it as a candidate - an operator's own note left
        // in the directory is not this mechanism's to delete.
        var foreignFile = Path.Combine(backupsDirectory, "operator-notes.txt");
        File.WriteAllText(foreignFile, "do not delete");

        var oldBackup = WriteSyntheticBackup(backupsDirectory, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Old");

        using var client = factory.CreateClient();

        Assert.True(File.Exists(foreignFile));

        // Retention of one: the old synthetic backup is pruned in favour of
        // the new real one, which proves the foreign file above was never
        // counted toward the limit at all - if it had been, it alone would
        // have satisfied the limit and this backup would have survived too.
        Assert.False(File.Exists(oldBackup));
    }

    [Fact]
    public void Startup_WhenABackupAttemptFails_PrunesNothing()
    {
        // Retention of one with two pre-existing backups: if pruning ran
        // regardless of success, or ran before the write instead of after,
        // one of these two would be gone even though the write below never
        // succeeds. Only "prune after success, never before" leaves both.
        using var factory = new KosyncWebApplicationFactory { FileBacked = true, MaxBackupsRetained = 1 };
        Directory.CreateDirectory(factory.DataDirectory);

        var dbPath = Path.Combine(factory.DataDirectory, Constants.Storage.DatabaseFileName);
        using (var writer = MigrationTestDatabase.OpenFileBackedAt(dbPath, SchemaOneMigrationBehindLatest))
        {
        }

        var backupsDirectory = Path.Combine(factory.DataDirectory, Constants.Storage.BackupsDirectoryName);
        var older = WriteSyntheticBackup(backupsDirectory, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Old1");
        var newer = WriteSyntheticBackup(backupsDirectory, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), "Old2");

        // Read-only, not a plain file in its place: Directory.CreateDirectory
        // is a no-op on a directory that already exists, so the pre-existing
        // backups above survive up to the write itself, which is exactly
        // where this attempt needs to fail.
        var writableMode = File.GetUnixFileMode(backupsDirectory);
        File.SetUnixFileMode(backupsDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        }
        finally
        {
            File.SetUnixFileMode(backupsDirectory, writableMode);
        }

        Assert.Equal(
            new[] { older, newer }.OrderBy(path => path),
            Directory.GetFiles(backupsDirectory).OrderBy(path => path));
    }

    private static string WriteSyntheticBackup(string backupsDirectory, DateTime utcTimestamp, string tag)
    {
        Directory.CreateDirectory(backupsDirectory);

        var path = Path.Combine(
            backupsDirectory, string.Format(Constants.Storage.BackupFileNameFormat, utcTimestamp, tag));

        File.WriteAllText(path, "synthetic backup content, never read back by these tests");

        return path;
    }
}
