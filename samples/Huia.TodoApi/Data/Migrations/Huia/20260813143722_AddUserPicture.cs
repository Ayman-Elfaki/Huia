using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Huia.TodoApi.Data.Migrations.Huia
{
    /// <inheritdoc />
    public partial class AddUserPicture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Picture",
                schema: "huia",
                table: "HuiaUsers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Picture",
                schema: "huia",
                table: "HuiaUsers");
        }
    }
}
