using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Huia.TodoApi.Data.Migrations.Huia
{
    /// <inheritdoc />
    public partial class AddPasswordlessPhoneFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhoneNumber",
                schema: "huia",
                table: "HuiaUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordlessLoginEnabled",
                schema: "huia",
                table: "HuiaUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaUsers_NormalizedPhoneNumber",
                schema: "huia",
                table: "HuiaUsers",
                column: "NormalizedPhoneNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HuiaUsers_NormalizedPhoneNumber",
                schema: "huia",
                table: "HuiaUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedPhoneNumber",
                schema: "huia",
                table: "HuiaUsers");

            migrationBuilder.DropColumn(
                name: "PasswordlessLoginEnabled",
                schema: "huia",
                table: "HuiaUsers");
        }
    }
}
