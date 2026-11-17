using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kosync.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentObservedMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataObservedAt",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservedAuthors",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservedFilename",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservedTitle",
                table: "Documents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetadataObservedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ObservedAuthors",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ObservedFilename",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ObservedTitle",
                table: "Documents");
        }
    }
}
