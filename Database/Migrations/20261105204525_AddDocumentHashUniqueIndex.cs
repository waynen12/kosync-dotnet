using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kosync.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentHashUniqueIndex : Migration
    {
        // One User never owns two Documents with the same hash (ADR 0008),
        // but until this index nothing said so, and the check-then-insert in
        // the push path could create a pair (issue #37). An existing database
        // may already hold one, and the index cannot be created over it - so
        // every pair is collapsed first, onto the oldest Document of the set.
        //
        // Oldest, because the lowest Id is the row the race inserted first:
        // it is the one every later push for that hash found and read against
        // until the pair broke it, so it carries the longer history.
        //
        // Raw SQL rather than the entities, deliberately: the state being
        // repaired is one the current model says cannot exist.
        private const string FindDuplicatesSql = """
            CREATE TEMPORARY TABLE "DuplicateDocuments" AS
            SELECT
                d."Id" AS "LoserId",
                d."BookId" AS "LoserBookId",
                (SELECT MIN(x."Id") FROM "Documents" x
                 WHERE x."UserId" = d."UserId" AND x."DocumentHash" = d."DocumentHash") AS "SurvivorId"
            FROM "Documents" d
            WHERE d."Id" > (SELECT MIN(x."Id") FROM "Documents" x
                            WHERE x."UserId" = d."UserId" AND x."DocumentHash" = d."DocumentHash");
            """;

        // Observed Metadata is per Document and the duplicates are one file,
        // so the survivor takes the newest observation of each field that
        // anybody made - ADR 0007's most-recent-observation tie-break, read
        // across a set of rows that should have been one. Per field, because
        // an absent value never erases a present one (ObservedMetadataRule),
        // and a device that reported a title but no author said nothing about
        // the author.
        //
        // MetadataObservedAt becomes the newest of them: it records when the
        // stored observation arrived, and the newest contributing row is when
        // that was.
        private const string CarryObservedMetadataSql = """
            UPDATE "Documents" SET
                "ObservedFilename" = (
                    SELECT g."ObservedFilename" FROM "Documents" g
                    WHERE g."UserId" = "Documents"."UserId" AND g."DocumentHash" = "Documents"."DocumentHash"
                      AND g."ObservedFilename" IS NOT NULL
                    ORDER BY g."MetadataObservedAt" IS NULL, g."MetadataObservedAt" DESC, g."Id" DESC
                    LIMIT 1),
                "ObservedTitle" = (
                    SELECT g."ObservedTitle" FROM "Documents" g
                    WHERE g."UserId" = "Documents"."UserId" AND g."DocumentHash" = "Documents"."DocumentHash"
                      AND g."ObservedTitle" IS NOT NULL
                    ORDER BY g."MetadataObservedAt" IS NULL, g."MetadataObservedAt" DESC, g."Id" DESC
                    LIMIT 1),
                "ObservedAuthors" = (
                    SELECT g."ObservedAuthors" FROM "Documents" g
                    WHERE g."UserId" = "Documents"."UserId" AND g."DocumentHash" = "Documents"."DocumentHash"
                      AND g."ObservedAuthors" IS NOT NULL
                    ORDER BY g."MetadataObservedAt" IS NULL, g."MetadataObservedAt" DESC, g."Id" DESC
                    LIMIT 1),
                "MetadataObservedAt" = (
                    SELECT MAX(g."MetadataObservedAt") FROM "Documents" g
                    WHERE g."UserId" = "Documents"."UserId" AND g."DocumentHash" = "Documents"."DocumentHash")
            WHERE "Id" IN (SELECT "SurvivorId" FROM "DuplicateDocuments");
            """;

        // Every push and every Progress Reset recorded against a losing
        // Document really happened to that file, so it moves onto the
        // survivor rather than being deleted with the row it hung off.
        private const string RepointHistorySql = """
            UPDATE "SyncEvents"
            SET "DocumentId" = (SELECT dd."SurvivorId" FROM "DuplicateDocuments" dd WHERE dd."LoserId" = "SyncEvents"."DocumentId")
            WHERE "DocumentId" IN (SELECT "LoserId" FROM "DuplicateDocuments");

            UPDATE "ProgressResets"
            SET "DocumentId" = (SELECT dd."SurvivorId" FROM "DuplicateDocuments" dd WHERE dd."LoserId" = "ProgressResets"."DocumentId")
            WHERE "DocumentId" IN (SELECT "LoserId" FROM "DuplicateDocuments");
            """;

        // Each duplicate carried its own current-progress pointer, and after
        // the repointing above the survivor would hold several - which the
        // schema allows and nothing else in the app expects (SyncEvent.
        // IsCurrent). The furthest-along one keeps it: within a single
        // Document, progress only becomes current by being at least as far
        // along as what it replaced, so the highest percentage is the only
        // pointer consistent with that history. Ties go to the most recent.
        private const string KeepOneCurrentProgressSql = """
            UPDATE "SyncEvents" SET "IsCurrent" = 0
            WHERE "IsCurrent" = 1
              AND "DocumentId" IN (SELECT "SurvivorId" FROM "DuplicateDocuments")
              AND "Id" <> (
                SELECT s."Id" FROM "SyncEvents" s
                WHERE s."DocumentId" = "SyncEvents"."DocumentId" AND s."IsCurrent" = 1
                ORDER BY s."Percentage" DESC, s."Timestamp" DESC, s."Id" DESC
                LIMIT 1);
            """;

        private const string RemoveLosersSql = """
            DELETE FROM "Documents" WHERE "Id" IN (SELECT "LoserId" FROM "DuplicateDocuments");
            """;

        // A losing Document's Book is left behind by the delete above. If it
        // has no other Documents it is a Book nobody can reach - removed the
        // way RemoveOrphanedBooks removes one after a Document delete - but
        // its Curated Metadata is a title a human typed, which no Document
        // observed and no push will bring back (ADR 0007). So it moves onto
        // the survivor's Book first, per field, and never over an answer that
        // Book already has.
        //
        // A losing Book that still has Documents of its own is a Book somebody
        // merged this file into. It stays, and keeps its own curated title:
        // the collapse is repairing one duplicated file, not undoing a merge.
        private const string CarryCuratedMetadataSql = """
            UPDATE "Books" SET
                "CuratedTitle" = COALESCE("CuratedTitle", (
                    SELECT lb."CuratedTitle" FROM "Books" lb
                    WHERE lb."Id" IN (
                            SELECT dd."LoserBookId" FROM "DuplicateDocuments" dd
                            JOIN "Documents" s ON s."Id" = dd."SurvivorId"
                            WHERE s."BookId" = "Books"."Id")
                      AND lb."Id" <> "Books"."Id"
                      AND lb."CuratedTitle" IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM "Documents" d WHERE d."BookId" = lb."Id")
                    ORDER BY lb."Id"
                    LIMIT 1)),
                "CuratedAuthor" = COALESCE("CuratedAuthor", (
                    SELECT lb."CuratedAuthor" FROM "Books" lb
                    WHERE lb."Id" IN (
                            SELECT dd."LoserBookId" FROM "DuplicateDocuments" dd
                            JOIN "Documents" s ON s."Id" = dd."SurvivorId"
                            WHERE s."BookId" = "Books"."Id")
                      AND lb."Id" <> "Books"."Id"
                      AND lb."CuratedAuthor" IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM "Documents" d WHERE d."BookId" = lb."Id")
                    ORDER BY lb."Id"
                    LIMIT 1))
            WHERE "Id" IN (
                SELECT s."BookId" FROM "Documents" s
                JOIN "DuplicateDocuments" dd ON dd."SurvivorId" = s."Id");
            """;

        private const string RemoveOrphanedBooksSql = """
            DELETE FROM "Books"
            WHERE "Id" IN (SELECT "LoserBookId" FROM "DuplicateDocuments")
              AND NOT EXISTS (SELECT 1 FROM "Documents" d WHERE d."BookId" = "Books"."Id");
            """;

        private const string ForgetDuplicatesSql = """
            DROP TABLE "DuplicateDocuments";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order matters throughout: the metadata and the history are read
            // off the losing rows, so they move before those rows are deleted,
            // and a Book is only orphaned once its Document is gone.
            migrationBuilder.Sql(FindDuplicatesSql);
            migrationBuilder.Sql(CarryObservedMetadataSql);
            migrationBuilder.Sql(RepointHistorySql);
            migrationBuilder.Sql(KeepOneCurrentProgressSql);
            migrationBuilder.Sql(RemoveLosersSql);
            migrationBuilder.Sql(CarryCuratedMetadataSql);
            migrationBuilder.Sql(RemoveOrphanedBooksSql);
            migrationBuilder.Sql(ForgetDuplicatesSql);

            // Redundant once the composite exists: SQLite can use a
            // multi-column index for a lookup on its leading column, which is
            // all this one was ever for.
            migrationBuilder.DropIndex(
                name: "IX_Documents_UserId",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId_DocumentHash",
                table: "Documents",
                columns: new[] { "UserId", "DocumentHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the index goes back. The collapse is not reversible and
            // deliberately is not reversed: the duplicates it removed were a
            // Book that could not sync, be read, or be deleted, and putting
            // them back would break it again.
            migrationBuilder.DropIndex(
                name: "IX_Documents_UserId_DocumentHash",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId",
                table: "Documents",
                column: "UserId");
        }
    }
}
