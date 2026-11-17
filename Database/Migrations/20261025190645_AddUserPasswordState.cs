using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kosync.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The column default is Provisional, which is the fail-closed way
            // round for anything inserting a User without saying.
            migrationBuilder.AddColumn<int>(
                name: "PasswordState",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Existing rows are the exception. Every User on an installed
            // server already has a password that works, and upgrading the
            // server is not a reason to lock them out of their own account -
            // so they are taken as UserChosen. ADR 0006 governs accounts
            // created from here on, not accounts that already exist.
            migrationBuilder.Sql("""UPDATE "Users" SET "PasswordState" = 1""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordState",
                table: "Users");
        }
    }
}
