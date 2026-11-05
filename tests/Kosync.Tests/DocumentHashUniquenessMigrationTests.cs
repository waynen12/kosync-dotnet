using Kosync.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kosync.Tests;

// One User never owns two Documents with the same hash (ADR 0008, issue
// #37). An existing database can already hold a pair, so the migration that
// adds the index has to collapse what it finds rather than refusing to run.
//
// These tests migrate a database to the schema immediately before it, write
// the duplicate rows the old schema allowed, and then migrate forward
// (MigrationTestDatabase).
public class DocumentHashUniquenessMigrationTests
{
    // The migration this one follows. Rows are written against its schema,
    // where nothing stops a second Document with the same hash.
    private const string SchemaBeforeUniqueDocumentHash = "AddProgressResets";

    private const string OneReader = """
        INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator", "PasswordState") VALUES ('reader', 'x', 1, 0, 0);
        """;

    // The pair the old schema allowed: one hash, one User, two rows - each
    // with the 1:1 Book the push path gives a brand-new Document (ADR 0001).
    // Document 2 is the loser, and it has a Progress Reset in its history,
    // because a reader went on using whichever of the pair their device
    // happened to be pushing against.
    private const string TwoDocumentsWithOneHash = """
        INSERT INTO "Books" ("UserId") VALUES (1);
        INSERT INTO "Books" ("UserId") VALUES (1);
        INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 1);
        INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 2);
        INSERT INTO "ProgressResets" ("DocumentId", "ResetAt") VALUES (2, '2026-10-03 08:00:00');
        """;

    [Fact]
    public void Migrating_LeavesADatabaseThatRefusesASecondDocumentWithTheSameHash()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute(OneReader);
        connection.MigrateToLatest();

        connection.Execute("""
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 1);
            """);

        Assert.Throws<SqliteException>(() => connection.Execute("""
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 2);
            """));
    }

    // The index cannot be created over a pair, so the pair has to go first.
    // The survivor is the oldest - the lowest Id, which is the one the race
    // in issue #37 inserted first and the one every reader has been reading
    // against since.
    [Fact]
    public async Task Migrating_CollapsesADuplicatePairOntoTheOldestDocument()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 2);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var document = Assert.Single(await db.Documents.ToListAsync());

        Assert.Equal(1, document.Id);
        Assert.Equal("hash-1", document.DocumentHash);
    }

    // Every push against a losing Document was a real reading session on
    // that file, so it moves onto the survivor rather than going with the
    // row it hung off.
    [Fact]
    public async Task Migrating_MovesTheLosingDocumentsSyncEventsOntoTheSurvivor()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            {TwoDocumentsWithOneHash}
            INSERT INTO "Devices" ("DeviceId", "DeviceName", "UserId") VALUES ('device-1', 'Kobo', 1);
            INSERT INTO "Devices" ("DeviceId", "DeviceName", "UserId") VALUES ('device-2', 'Boox', 1);
            INSERT INTO "SyncEvents" ("DocumentId", "DeviceId", "Progress", "Percentage", "Timestamp", "IsCurrent") VALUES (1, 1, 'p1', 0.2, '2026-10-01 12:00:00', 1);
            INSERT INTO "SyncEvents" ("DocumentId", "DeviceId", "Progress", "Percentage", "Timestamp", "IsCurrent") VALUES (2, 2, 'p2', 0.6, '2026-10-02 12:00:00', 1);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var events = await db.SyncEvents.OrderBy(s => s.Id).ToListAsync();

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(1, e.DocumentId));
    }

    // Both duplicates held their own current-progress pointer, and the
    // survivor may hold only one (SyncEvent.IsCurrent). The furthest-along
    // keeps it: within one Document, progress becomes current only by being
    // at least as far along as what it replaced.
    [Fact]
    public async Task Migrating_LeavesTheFurthestAlongPushAsTheOnlyCurrentProgress()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            {TwoDocumentsWithOneHash}
            INSERT INTO "Devices" ("DeviceId", "DeviceName", "UserId") VALUES ('device-1', 'Kobo', 1);
            INSERT INTO "Devices" ("DeviceId", "DeviceName", "UserId") VALUES ('device-2', 'Boox', 1);
            INSERT INTO "SyncEvents" ("DocumentId", "DeviceId", "Progress", "Percentage", "Timestamp", "IsCurrent") VALUES (1, 1, 'p1', 0.8, '2026-10-01 12:00:00', 1);
            INSERT INTO "SyncEvents" ("DocumentId", "DeviceId", "Progress", "Percentage", "Timestamp", "IsCurrent") VALUES (2, 2, 'p2', 0.6, '2026-10-02 12:00:00', 1);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var current = Assert.Single(await db.SyncEvents.Where(s => s.IsCurrent).ToListAsync());

        Assert.Equal(0.8m, current.Percentage);
    }

    // A Progress Reset is a distinct event in a Document's history (GLOSSARY
    // "Progress Reset") and the reader really did restart that file, so it
    // moves across with the pushes.
    [Fact]
    public async Task Migrating_MovesTheLosingDocumentsProgressResetsOntoTheSurvivor()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            {TwoDocumentsWithOneHash}
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var reset = Assert.Single(await db.ProgressResets.ToListAsync());

        Assert.Equal(1, reset.DocumentId);
    }

    // Observed Metadata is per Document and these rows are one file, so the
    // survivor takes the newest observation of each field anybody made - and
    // per field, because a device that reported a title but no author said
    // nothing about the author (ADR 0007, ObservedMetadataRule).
    [Fact]
    public async Task Migrating_GivesTheSurvivorTheNewestObservationOfEachField()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId", "ObservedFilename", "ObservedTitle", "ObservedAuthors", "MetadataObservedAt")
                VALUES ('hash-1', 1, 1, 'dune.epub', 'Dune', 'Frank Herbert', '2026-10-01 12:00:00');
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId", "ObservedFilename", "ObservedTitle", "ObservedAuthors", "MetadataObservedAt")
                VALUES ('hash-1', 1, 2, 'dune-converted.epub', NULL, NULL, '2026-10-05 09:30:00');
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var document = Assert.Single(await db.Documents.ToListAsync());

        Assert.Equal("dune-converted.epub", document.ObservedFilename);
        Assert.Equal("Dune", document.ObservedTitle);
        Assert.Equal("Frank Herbert", document.ObservedAuthors);
        Assert.Equal(new DateTime(2026, 10, 5, 9, 30, 0, DateTimeKind.Utc), document.MetadataObservedAt);
    }

    // The losing Document's Book is left with nothing in it, and a Book no
    // Document belongs to is unreachable - removed here the way
    // RemoveOrphanedBooks removes one after a Document delete.
    [Fact]
    public async Task Migrating_RemovesABookLeftWithNoDocuments()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            {TwoDocumentsWithOneHash}
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var book = Assert.Single(await db.Books.ToListAsync());

        Assert.Equal(1, book.Id);
    }

    // A Curated title is something a human typed about a Book, and the Book
    // holding it is about to be removed - nothing observed it and no push
    // will bring it back (ADR 0007). So it moves onto the survivor's Book,
    // the same way a merge moves one.
    [Fact]
    public async Task Migrating_MovesACuratedTitleOffABookItIsAboutToRemove()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Books" ("UserId", "CuratedTitle", "CuratedAuthor") VALUES (1, 'Dune', 'Frank Herbert');
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 2);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var book = Assert.Single(await db.Books.ToListAsync());

        Assert.Equal("Dune", book.CuratedTitle);
        Assert.Equal("Frank Herbert", book.CuratedAuthor);
    }

    // Never over an answer the surviving Book already has: two curated
    // titles are two human answers about what is now one Book, and the one
    // on the Book being kept is the one to keep (ADR 0007).
    [Fact]
    public async Task Migrating_KeepsTheSurvivingBooksOwnCuratedTitle()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            INSERT INTO "Books" ("UserId", "CuratedTitle") VALUES (1, 'Dune');
            INSERT INTO "Books" ("UserId", "CuratedTitle") VALUES (1, 'Dune (converted)');
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 2);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var book = Assert.Single(await db.Books.ToListAsync());

        Assert.Equal("Dune", book.CuratedTitle);
    }

    // A losing Book that still holds Documents of its own is a Book somebody
    // merged this file into. The collapse is repairing one duplicated file,
    // not undoing a merge, so that Book stays - with its curated title.
    [Fact]
    public async Task Migrating_LeavesABookThatStillHasOtherDocuments()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute($"""
            {OneReader}
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Books" ("UserId", "CuratedTitle") VALUES (1, 'Dune');
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 2);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-2', 1, 2);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        var books = await db.Books.OrderBy(b => b.Id).ToListAsync();

        Assert.Equal([1, 2], books.Select(b => b.Id));
        Assert.Null(books[0].CuratedTitle);
        Assert.Equal("Dune", books[1].CuratedTitle);
    }

    // Two Users reading the same file is two Documents with the same hash and
    // nothing wrong: the invariant is per User, like the one on a Device.
    [Fact]
    public async Task Migrating_LeavesTwoUsersWithTheSameHashAlone()
    {
        using var connection = MigrationTestDatabase.OpenAt(SchemaBeforeUniqueDocumentHash);

        connection.Execute("""
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator", "PasswordState") VALUES ('reader', 'x', 1, 0, 0);
            INSERT INTO "Users" ("Username", "PasswordHash", "IsActive", "IsAdministrator", "PasswordState") VALUES ('other', 'x', 1, 0, 0);
            INSERT INTO "Books" ("UserId") VALUES (1);
            INSERT INTO "Books" ("UserId") VALUES (2);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 1, 1);
            INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ('hash-1', 2, 2);
            """);

        connection.MigrateToLatest();

        using var db = connection.ReadEverythingIn();

        Assert.Equal(2, await db.Documents.CountAsync());
        Assert.Equal(2, await db.Books.CountAsync());
    }
}
