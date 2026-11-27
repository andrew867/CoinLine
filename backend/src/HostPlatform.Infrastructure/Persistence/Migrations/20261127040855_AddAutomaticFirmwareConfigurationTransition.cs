using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticFirmwareConfigurationTransition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ActivatedAutomatically",
                table: "FirmwareConfigurationTransitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoActivateOnContact",
                table: "FirmwareConfigurationTransitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivatedAutomatically",
                table: "FirmwareConfigurationTransitions");

            migrationBuilder.DropColumn(
                name: "AutoActivateOnContact",
                table: "FirmwareConfigurationTransitions");
        }
    }
}
