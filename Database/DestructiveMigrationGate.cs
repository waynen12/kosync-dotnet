namespace Kosync.Database;

// ADR 0009: a Destructive Migration never runs unattended. Checked from
// KosyncDbInitializer immediately before Migrate() - the same place task 2's
// pre-migration backup belongs, and for the same reason: a deploy script
// could skip either, but nothing that calls Migrate() can bypass this.
public static class DestructiveMigrationGate
{
    public static void EnsureNoUnattendedDestructiveMigration(KosyncDbContext db, ILogger logger)
    {
        // Checked before anything that would touch the connection:
        // GetPendingMigrations() below opens it, and SQLite creates a file
        // that does not yet exist the moment it is opened. Reading
        // DataSource first, on a closed connection, costs nothing and
        // decides the skip without that side effect.
        var dataSource = db.Database.GetDbConnection().DataSource;

        // Not file-backed (the test suite's `:memory:` databases) or not
        // yet created (a fresh install): there is nothing destructive that
        // can happen to a database that does not exist yet, and every
        // migration is trivially "pending" against one - the same
        // fresh-install rule ADR 0009 already commits to, derived from the
        // connection's own data source rather than a path guess.
        if (string.IsNullOrEmpty(dataSource) || !File.Exists(dataSource))
        {
            return;
        }

        var unattended = UnattendedDestructiveMigrations(db.Database.GetPendingMigrations());

        if (unattended.Count == 0)
        {
            return;
        }

        foreach (var id in unattended)
        {
            logger.LogError(
                Constants.Migrations.RefusalLogFormat,
                id,
                Constants.Migrations.DestructiveDescriptions[id],
                Constants.EnvironmentVariables.AllowDestructiveMigrations,
                id);
        }

        var detail = string.Join(", ", unattended.Select(id => string.Format(
            Constants.Migrations.RefusalExceptionEntryFormat,
            id,
            Constants.Migrations.DestructiveDescriptions[id])));

        throw new InvalidOperationException(string.Format(
            Constants.Migrations.RefusalExceptionFormat,
            Constants.EnvironmentVariables.AllowDestructiveMigrations,
            detail));
    }

    // Pure beyond the environment-variable read: which of the pending ids
    // are both declared and not permitted. Pulled out of the method above
    // so this - the actual decision - is testable against synthetic ids,
    // rather than only through a database stopped at one exact point in the
    // real migration chain, where the newest migration happening to be
    // Destructive would always be among the pending set too.
    public static List<string> UnattendedDestructiveMigrations(IEnumerable<string> pendingMigrationIds)
    {
        var permitted = PermittedMigrationIds();

        return pendingMigrationIds
            .Where(id => Constants.Migrations.Destructive.Contains(id) && !permitted.Contains(id))
            .ToList();
    }

    private static HashSet<string> PermittedMigrationIds()
    {
        var value = Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.AllowDestructiveMigrations);

        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
    }
}
