using Microsoft.Data.Sqlite;

namespace Kosync.Database;

// ADR 0009: a migration never applies without a backup first. Checked from
// KosyncDbInitializer immediately before Migrate() - the same place the
// Destructive Migration gate (issue #49) belongs, and for the same reason:
// a deploy script could skip either, but nothing that calls Migrate() can
// bypass this.
public static class DatabaseBackup
{
    public static void BackupBeforeMigrating(KosyncDbContext db, string dataDirectory, ILogger logger)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();

        // Checked on a closed connection, before anything that would open
        // it: GetPendingMigrations() below does, and SQLite creates a file
        // that does not yet exist the moment it is opened. Not file-backed
        // (the test suite's `:memory:` databases) or not yet created (a
        // fresh install) - there is nothing to back up, which is the same
        // fresh-install rule the gate applies, derived from the connection's
        // own data source rather than a path guess.
        var dataSource = connection.DataSource;

        if (string.IsNullOrEmpty(dataSource) || !File.Exists(dataSource))
        {
            return;
        }

        var pending = db.Database.GetPendingMigrations().ToList();

        // A backup on every restart is noise, and worse, it makes each one
        // meaningless: the useful artefact is the state immediately before a
        // schema change, not a snapshot of nothing changing.
        if (pending.Count == 0)
        {
            return;
        }

        var backupsDirectory = Path.Combine(dataDirectory, Constants.Storage.BackupsDirectoryName);

        // Named for the migration about to apply - the first pending one,
        // not every pending one - so the file says what moment it precedes.
        var backupPath = Path.Combine(
            backupsDirectory,
            string.Format(Constants.Storage.BackupFileNameFormat, DateTime.UtcNow, pending[0]));

        try
        {
            Directory.CreateDirectory(backupsDirectory);

            // Opened here only if it was not already: GetPendingMigrations()
            // above may have already left it open, and closing a connection
            // this method does not own would break the Migrate() call that
            // follows it.
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere)
            {
                connection.Open();
            }

            try
            {
                // SQLite's own online backup API: a page-level copy that
                // folds in anything still sitting in the write-ahead log, so
                // a database with uncheckpointed writes still backs up
                // whole. A plain file copy would silently miss them.
                using var destination = new SqliteConnection($"Data Source={backupPath}");
                destination.Open();

                connection.BackupDatabase(destination);
            }
            finally
            {
                if (openedHere)
                {
                    connection.Close();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, Constants.Storage.BackupFailedLogFormat, backupPath, string.Join(", ", pending));

            // Throw, not Environment.Exit: initialization already runs
            // before the request pipeline is built, so a throw here serves
            // no requests and exits non-zero, which is what ADR 0009
            // requires - and a process exit call would terminate the test
            // host that exercises this very path.
            throw new InvalidOperationException(
                string.Format(Constants.Storage.BackupFailedExceptionFormat, backupPath, ex.Message), ex);
        }

        logger.LogInformation(Constants.Storage.BackupWrittenLogFormat, backupPath, string.Join(", ", pending));
    }
}
