using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kosync.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressResets : Migration
    {
        // A Progress Reset used to be a single timestamp on the Document it
        // happened to, so a Document reset twice remembered only the latest
        // one (issue #40). It becomes a row per occurrence here, and the
        // timestamp column goes - but not before every reset it does hold is
        // carried across, one row each. A Document that was never reset has
        // nothing to carry and gets no row.
        private const string CarryForwardSql = """
            INSERT INTO "ProgressResets" ("DocumentId", "ResetAt")
            SELECT "Id", "LastResetAt" FROM "Documents" WHERE "LastResetAt" IS NOT NULL;
            """;

        // Going back, a Document with several resets can keep only one, and
        // the one the old column meant is the newest: LastResetAt was "when
        // this Document was last reset". The earlier resets are lost, which
        // is what reverting to a single-timestamp schema costs.
        private const string CarryBackSql = """
            UPDATE "Documents"
            SET "LastResetAt" = (SELECT MAX("ResetAt") FROM "ProgressResets" WHERE "DocumentId" = "Documents"."Id");
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProgressResets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResetAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressResets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgressResets_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProgressResets_DocumentId",
                table: "ProgressResets",
                column: "DocumentId");

            // Between the table existing and the column being dropped: the
            // resets have nowhere to go before the first, and no source after
            // the second.
            migrationBuilder.Sql(CarryForwardSql);

            migrationBuilder.DropColumn(
                name: "LastResetAt",
                table: "Documents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastResetAt",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(CarryBackSql);

            migrationBuilder.DropTable(
                name: "ProgressResets");
        }
    }
}
