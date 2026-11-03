using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kosync.Tests;

// A Progress Reset was a single Documents.LastResetAt timestamp before issue
// #40 and is a ProgressResets row per occurrence after it. The reset an
// existing database remembers is a real thing that happened to a real
// reader's book, so AddProgressResets has to carry it across rather than
// start the new table empty.
//
// These tests migrate a database to the schema immediately before it, write
// rows the old way, and then migrate forward (MigrationTestDatabase).
public class ProgressResetMigrationTests
{
    // The migration this one follows. Rows are written against its schema,
    // where a Document carries its own "LastResetAt" column.
    private const string SchemaBeforeProgressResets = "AddBookCuratedMetadata";

    [Fact]
    public async Task Migrating_TurnsEachDocumentsLastResetAtIntoAProgressReset()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeProgressResets);

        connection.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator", "PasswordState") VALUES ('reader', 'x', 1, 0, 0);
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId", "LastResetAt") VALUES ('hash-1', 1, 1, '2026-10-01 12:00:00');
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var reset = Assert.Single(await db.ProgressResets.ToListAsync());

        Assert.Equal(1, reset.DocumentId);
        Assert.Equal(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), reset.ResetAt);
    }

    // Nothing to carry: a Document nobody ever reset had a null timestamp,
    // and inventing a reset for it would put a marker in its history saying
    // the reader restarted a book they did not.
    [Fact]
    public async Task Migrating_ADocumentNeverReset_RecordsNoProgressReset()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeProgressResets);

        connection.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator", "PasswordState") VALUES ('reader', 'x', 1, 0, 0);
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId", "LastResetAt") VALUES ('hash-1', 1, 1, NULL);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        Assert.Empty(await db.ProgressResets.ToListAsync());
    }

    // The old column goes, so the schema cannot hold two answers to when a
    // Document was reset - which was the whole defect (issue #40).
    [Fact]
    public void Migrating_LeavesNoLastResetAtColumnBehind()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeProgressResets);

        connection.MigrateToLatest();

        Assert.DoesNotContain("LastResetAt", connection.ColumnNamesOf("Documents"));
    }

    // Reverting puts the single timestamp back, and a Document with several
    // resets can keep only one there: the newest, which is what
    // "LastResetAt" meant. The earlier resets are lost - that is the cost of
    // going back to a schema with room for one.
    [Fact]
    public void Reverting_KeepsTheNewestResetAsLastResetAt()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeProgressResets);

        connection.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator", "PasswordState") VALUES ('reader', 'x', 1, 0, 0);
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId", "LastResetAt") VALUES ('hash-1', 1, 1, NULL);
            """);

        connection.MigrateToLatest();

        connection.Execute("""
            INSERT INTO "ProgressResets" ("DocumentId", "ResetAt") VALUES (1, '2026-10-01 12:00:00');
            INSERT INTO "ProgressResets" ("DocumentId", "ResetAt") VALUES (1, '2026-10-05 09:30:00');
            """);

        connection.MigrateTo(SchemaBeforeProgressResets);

        Assert.Equal(
            "2026-10-05 09:30:00",
            connection.SingleValueOf("""SELECT "LastResetAt" FROM "Documents" WHERE "Id" = 1"""));
    }
}
