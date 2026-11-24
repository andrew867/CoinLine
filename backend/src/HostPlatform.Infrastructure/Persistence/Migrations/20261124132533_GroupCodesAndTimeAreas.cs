using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GroupCodesAndTimeAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Details_TimeAreaCode",
                table: "TerminalInventories",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "TerminalGroups",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "TerminalGroups",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Details_TimeAreaCode",
                table: "TerminalInventories");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "TerminalGroups");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "TerminalGroups");
        }
    }
}
