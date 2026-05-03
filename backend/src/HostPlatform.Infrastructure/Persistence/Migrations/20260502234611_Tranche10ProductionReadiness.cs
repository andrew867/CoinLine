using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tranche10ProductionReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FirmwareUpdateJobs_TerminalId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.AddColumn<string>(
                name: "ClientIdempotencyKey",
                table: "DownloadBatches",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubsystemHeartbeats",
                columns: table => new
                {
                    Subsystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubsystemHeartbeats", x => x.Subsystem);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateJobs_TerminalId_Status",
                table: "FirmwareUpdateJobs",
                columns: new[] { "TerminalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatches_ClientIdempotencyKey",
                table: "DownloadBatches",
                column: "ClientIdempotencyKey",
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL AND \"ClientIdempotencyKey\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CreatedAtUtc",
                table: "AuditEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TerminalId_CreatedAtUtc",
                table: "AuditEvents",
                columns: new[] { "TerminalId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubsystemHeartbeats");

            migrationBuilder.DropIndex(
                name: "IX_FirmwareUpdateJobs_TerminalId_Status",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropIndex(
                name: "IX_DownloadBatches_ClientIdempotencyKey",
                table: "DownloadBatches");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_CreatedAtUtc",
                table: "AuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_TerminalId_CreatedAtUtc",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "ClientIdempotencyKey",
                table: "DownloadBatches");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateJobs_TerminalId",
                table: "FirmwareUpdateJobs",
                column: "TerminalId");
        }
    }
}
