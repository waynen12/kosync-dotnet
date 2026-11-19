using Kosync.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kosync.Tests.TestSupport;

// What a test about a migration needs: a database stopped at the schema
// *before* the migration under test, rows written against that older schema
// the way the running server would have left them, and then the migration
// run forwards over them.
//
// Raw SQL throughout, deliberately. The entities describe today's schema, so
// writing a row the old way cannot go through them - and reading one back
// afterwards is exactly what the migration is supposed to have fixed.
public static class MigrationTestDatabase
{
    // A private in-memory database migrated as far as migrationName and no
    // further. The connection is the database: SQLite drops an in-memory one
    // when its last connection closes, so the caller owns it for the test.
    public static SqliteConnection OpenAt(string migrationName)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        MigrateTo(connection, migrationName);

        return connection;
    }

    // A file-backed database migrated as far as migrationName and no
    // further, at the given path - the one gap the in-memory helper above
    // cannot cover: a pre-migration backup test needs a real file, because
    // SQLite's write-ahead log is a property of the file, not of the
    // connection (issue #48). Closing every open connection to the file
    // triggers SQLite's automatic checkpoint-on-close, which would erase the
    // very WAL content such a test exists to prove survives - pair with
    // KeepFileAliveAt before closing the connection this returns, if that
    // matters to the test.
    public static SqliteConnection OpenFileBackedAt(string path, string migrationName)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        // Production's database is WAL-mode because the EF provider sets it
        // while creating a brand-new file (confirmed in
        // docs/deploy/phase5-findings.md). Migrating to a named target here
        // goes through IMigrator directly rather than through that
        // creation path, so the pragma is set explicitly to match what a
        // real deployment's file actually carries in its header.
        connection.Execute("PRAGMA journal_mode=WAL;");

        MigrateTo(connection, migrationName);

        return connection;
    }

    // A second connection to a file database that is already open, so that
    // closing some other connection to the same file does not drop the
    // count to zero - the condition under which SQLite checkpoints the
    // write-ahead log automatically.
    public static SqliteConnection KeepFileAliveAt(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        return connection;
    }

    public static void MigrateTo(this SqliteConnection connection, string migrationName)
    {
        using var db = TestDb.CreateSystemContext(OptionsFor(connection));
        db.GetService<IMigrator>().Migrate(migrationName);
    }

    public static void MigrateToLatest(this SqliteConnection connection)
    {
        using var db = TestDb.CreateSystemContext(OptionsFor(connection));
        db.Database.Migrate();
    }

    // A context over the migrated database that can see every row - the view
    // the assertions want, not any one User's (ADR 0004).
    public static KosyncDbContext ReadEverythingIn(this SqliteConnection connection)
    {
        return TestDb.CreateSystemContext(OptionsFor(connection));
    }

    public static void Execute(this SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // The raw stored value of a single column, for the assertions that cannot
    // go through the entities: a column the current model no longer has.
    public static string? SingleValueOf(this SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar() as string;
    }

    public static List<string> ColumnNamesOf(this SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";

        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static DbContextOptions<KosyncDbContext> OptionsFor(SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<KosyncDbContext>().UseSqlite(connection).Options;
    }
}
