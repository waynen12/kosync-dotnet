using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kosync.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddBookCuratedMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CuratedAuthor",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CuratedTitle",
                table: "Books",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CuratedAuthor",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CuratedTitle",
                table: "Books");
        }
    }
}
