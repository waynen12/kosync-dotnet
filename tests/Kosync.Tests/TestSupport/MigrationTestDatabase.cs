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
