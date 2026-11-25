using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledDownloadPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "ScheduledTableDownloads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreparedAtUtc",
                table: "ScheduledTableDownloads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleId",
                table: "DownloadBatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "DownloadBatches" AS batch
                SET "ScheduleId" = schedule."Id"
                FROM "ScheduledTableDownloads" AS schedule
                WHERE schedule."LastBatchId" = batch."Id";

                UPDATE "ScheduledTableDownloads" AS schedule
                SET "PreparedAtUtc" = batch."CreatedAtUtc"
                FROM "DownloadBatches" AS batch
                WHERE schedule."LastBatchId" = batch."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_AvailableAtUtc",
                table: "ScheduledTableDownloads",
                column: "AvailableAtUtc",
                filter: "\"LastBatchId\" IS NULL AND \"NccSessionId\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledTableDownloads_AvailableAtUtc",
                table: "ScheduledTableDownloads");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "ScheduledTableDownloads");

            migrationBuilder.DropColumn(
                name: "PreparedAtUtc",
                table: "ScheduledTableDownloads");

            migrationBuilder.DropColumn(
                name: "ScheduleId",
                table: "DownloadBatches");
        }
    }
}
