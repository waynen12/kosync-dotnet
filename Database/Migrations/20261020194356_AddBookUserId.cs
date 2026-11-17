using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kosync.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddBookUserId : Migration
    {
        // Books previously had no owner at all - a Book was reachable only
        // through its Documents - so the column has to be backfilled from the
        // Documents that point at it (ADR 0004).
        //
        // Two edge cases, both deliberately not silent:
        //
        //  - A Book with no Documents has neither content nor an owner to
        //    derive, so it is deleted. There should not be one: every code
        //    path that removes a Document already sweeps up the Book it
        //    orphaned.
        //
        //  - A Book whose Documents span more than one User cannot be assigned
        //    an owner without picking a winner, and picking would move one
        //    User's Book under another User's name. There is no code path that
        //    can produce one today, so rather than guessing, the migration
        //    aborts and leaves the database untouched. The staging table below
        //    is how: its CHECK constraint is named with
        //    Constants.Tenancy.BookOwnershipConflictMessage, because SQLite
        //    reports a failed named CHECK by name and that is the only way raw
        //    migration SQL gets to say anything legible.
        private static readonly string BackfillSql = $"""
            DELETE FROM "Books"
            WHERE NOT EXISTS (SELECT 1 FROM "Documents" WHERE "Documents"."BookId" = "Books"."Id");

            CREATE TABLE "BookOwnershipBackfill" (
                "BookId" INTEGER NOT NULL PRIMARY KEY,
                "UserId" INTEGER NOT NULL,
                "OwnerCount" INTEGER NOT NULL,
                CONSTRAINT "{Constants.Tenancy.BookOwnershipConflictMessage}" CHECK ("OwnerCount" = 1)
            );

            INSERT INTO "BookOwnershipBackfill" ("BookId", "UserId", "OwnerCount")
            SELECT "BookId", MIN("UserId"), COUNT(DISTINCT "UserId")
            FROM "Documents"
            GROUP BY "BookId";

            UPDATE "Books"
            SET "UserId" = (SELECT "UserId" FROM "BookOwnershipBackfill" WHERE "BookId" = "Books"."Id");

            DROP TABLE "BookOwnershipBackfill";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Books",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Must run before the foreign key below: SQLite cannot add a
            // constraint in place, so EF rebuilds the Books table to apply it
            // and every row has to already carry a real owner by then.
            migrationBuilder.Sql(BackfillSql);

            migrationBuilder.CreateIndex(
                name: "IX_Books_UserId",
                table: "Books",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Users_UserId",
                table: "Books",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Users_UserId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_UserId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Books");
        }
    }
}
