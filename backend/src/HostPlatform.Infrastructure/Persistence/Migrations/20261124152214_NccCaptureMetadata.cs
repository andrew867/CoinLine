using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NccCaptureMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "LinkControl",
                table: "NccSessionFrames",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SentByHost",
                table: "NccSessionFrames",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WireLength",
                table: "NccSessionFrames",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinkControl",
                table: "NccSessionFrames");

            migrationBuilder.DropColumn(
                name: "SentByHost",
                table: "NccSessionFrames");

            migrationBuilder.DropColumn(
                name: "WireLength",
                table: "NccSessionFrames");
        }
    }
}
