using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBoundedCodeResume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IncomingAttemptCount",
                table: "FirmwareUpdateJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IncomingAttemptLimit",
                table: "FirmwareUpdateJobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IncomingRetryEligible",
                table: "FirmwareUpdateJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncomingAttemptCount",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "IncomingAttemptLimit",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "IncomingRetryEligible",
                table: "FirmwareUpdateJobs");
        }
    }
}
