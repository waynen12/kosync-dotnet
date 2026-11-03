using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kosync.Tests;

// Book had no owner before ADR 0004 - it was reachable only through its
// Documents - so AddBookUserId has to derive one for every existing row.
// These tests migrate a database to the schema immediately before it, write
// rows the old way, and then migrate forward (MigrationTestDatabase).
public class BookUserIdMigrationTests
{
    // The migration this one follows. Rows are written against its schema,
    // where "Books" is a bare Id column.
    private const string SchemaBeforeBookUserId = "AddDocumentLastResetAt";

    [Fact]
    public async Task Migrating_BackfillsEachBooksUserIdFromItsDocuments()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeBookUserId);

        connection.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('reader-a', 'x', 1, 0);
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('reader-b', 'x', 1, 0);
            INSERT INTO "Books" DEFAULT VALUES;
            INSERT INTO "Books" DEFAULT VALUES;
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-a', 1, 1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-b', 2, 2);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        Assert.Equal(1, (await db.Books.SingleAsync(b => b.Id == 1)).UserId);
        Assert.Equal(2, (await db.Books.SingleAsync(b => b.Id == 2)).UserId);
    }

    // A Book shared by two Documents of the same User - the Split Book case,
    // which is the whole reason Books and Documents are separate - keeps that
    // one owner.
    [Fact]
    public async Task Migrating_AMergedBook_TakesTheOwnerItsDocumentsShare()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeBookUserId);

        connection.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('ignored', 'x', 1, 0);
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('reader', 'x', 1, 0);
            INSERT INTO "Books" DEFAULT VALUES;
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 2, 1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-2', 2, 1);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        Assert.Equal(2, (await db.Books.SingleAsync()).UserId);
    }

    // There should not be one: every path that removes a Document already
    // sweeps up the Book it orphaned. If there is, it has neither content nor
    // an owner to derive, so it goes.
    [Fact]
    public async Task Migrating_ABookWithNoDocuments_RemovesIt()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeBookUserId);

        connection.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('reader', 'x', 1, 0);
            INSERT INTO "Books" DEFAULT VALUES;
            INSERT INTO "Books" DEFAULT VALUES;
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 2);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var book = Assert.Single(await db.Books.ToListAsync());
        Assert.Equal(2, book.Id);
    }

    // No code path can produce a Book whose Documents span Users, so picking
    // an owner for one would be guessing - and guessing wrong moves one
    // User's Book under another User's name. The migration says so and leaves
    // the database alone instead.
    [Fact]
    public void Migrating_ABookWhoseDocumentsSpanUsers_AbortsAndChangesNothing()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeBookUserId);

        connection.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('reader-a', 'x', 1, 0);
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator") VALUES ('reader-b', 'x', 1, 0);
            INSERT INTO "Books" DEFAULT VALUES;
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-a', 1, 1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-b', 2, 1);
            """);

        var exception = Assert.ThrowsAny<Exception>(() => connection.MigrateToLatest());

        Assert.Contains(Constants.Tenancy.BookOwnershipConflictMessage, exception.Message);

        // Rolled back: the column the migration was adding is not there, so
        // the operator can fix the data and run it again.
        Assert.DoesNotContain("UserId", connection.ColumnNamesOf("Books"));
    }
}
