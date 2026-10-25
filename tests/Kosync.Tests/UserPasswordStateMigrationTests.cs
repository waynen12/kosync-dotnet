using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kosync.Tests;

// What AddUserPasswordState does to a server that is already running. Every
// User on it has a password that works, and upgrading is not a reason to lock
// anybody out of their own account - so they are taken as UserChosen, while
// the column's own default stays Provisional for anything created later
// (ADR 0006).
public class UserPasswordStateMigrationTests
{
    // The migration this one follows. Rows are written against its schema,
    // where "Users" has no PasswordState column at all.
    private const string SchemaBeforePasswordState = "AddBookUserId";

    [Fact]
    public async Task Migrating_TakesEveryExistingUsersPasswordAsTheirOwn()
    {
        using var connection = OpenDatabaseAt(SchemaBeforePasswordState);

        Execute(connection, """
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('admin', 'x', 1, 1);
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('reader', 'x', 1, 0);
            """);

        MigrateToLatest(connection);

        using var db = TestDb.CreateSystemContext(OptionsFor(connection));

        Assert.All(await db.Users.ToListAsync(), user =>
        {
            Assert.Equal(PasswordState.UserChosen, user.PasswordState);
            Assert.False(user.MustChangePassword);
        });
    }

    // The column default is the other way round, so a User written by
    // anything that does not state a PasswordState - a hand-rolled INSERT,
    // a future code path that forgets - gets the state that grants nothing
    // rather than the one that grants everything.
    [Fact]
    public async Task AfterMigrating_AUserInsertedWithoutAPasswordState_IsProvisional()
    {
        using var connection = OpenDatabaseAt(SchemaBeforePasswordState);

        MigrateToLatest(connection);

        Execute(connection, """
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('reader', 'x', 1, 0);
            """);

        using var db = TestDb.CreateSystemContext(OptionsFor(connection));
        var reader = await db.Users.SingleAsync(u => u.Username == "reader");

        Assert.Equal(PasswordState.Provisional, reader.PasswordState);
        Assert.True(reader.MustChangePassword);
    }

    private static SqliteConnection OpenDatabaseAt(string migrationName)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var db = TestDb.CreateSystemContext(OptionsFor(connection));
        db.GetService<IMigrator>().Migrate(migrationName);

        return connection;
    }

    private static void MigrateToLatest(SqliteConnection connection)
    {
        using var db = TestDb.CreateSystemContext(OptionsFor(connection));
        db.Database.Migrate();
    }

    private static DbContextOptions<KosyncDbContext> OptionsFor(SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<KosyncDbContext>().UseSqlite(connection).Options;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
