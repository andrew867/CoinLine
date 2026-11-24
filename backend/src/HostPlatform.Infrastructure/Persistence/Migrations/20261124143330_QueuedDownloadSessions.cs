using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QueuedDownloadSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NccSessionId",
                table: "DownloadBatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatches_NccSessionId",
                table: "DownloadBatches",
                column: "NccSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatches_TerminalId_ActiveTransfer",
                table: "DownloadBatches",
                column: "TerminalId",
                unique: true,
                filter: "\"NccSessionId\" IS NOT NULL AND \"Status\" = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadBatches_NccSessions_NccSessionId",
                table: "DownloadBatches",
                column: "NccSessionId",
                principalTable: "NccSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Older operator batches were marked Running before a terminal started sending ACKs.
            migrationBuilder.Sql("""
                UPDATE "DownloadBatches" AS batch
                SET "Status" = 0
                WHERE batch."Status" = 1 AND batch."CompletedAtUtc" IS NULL AND batch."LastError" IS NULL
                  AND EXISTS (
                    SELECT 1 FROM "AuditEvents" AS audit
                    WHERE audit."Category" = 'tables.download' AND audit."Action" IN ('start', 'retry')
                      AND audit."Resource" = batch."Id"::text
                  )
                  AND EXISTS (
                    SELECT 1 FROM "DownloadBatchItems" AS item WHERE item."DownloadBatchId" = batch."Id"
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM "DownloadBatchItems" AS item WHERE item."DownloadBatchId" = batch."Id"
                      AND (item."HostDownloadPhase" <> 3 OR item."ItemStatus" <> 1 OR item."Succeeded"
                        OR item."LastAckStatus" <> 'pending_terminal_ack')
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DownloadBatches_NccSessions_NccSessionId",
                table: "DownloadBatches");

            migrationBuilder.DropIndex(
                name: "IX_DownloadBatches_NccSessionId",
                table: "DownloadBatches");

            migrationBuilder.DropIndex(
                name: "IX_DownloadBatches_TerminalId_ActiveTransfer",
                table: "DownloadBatches");

            migrationBuilder.DropColumn(
                name: "NccSessionId",
                table: "DownloadBatches");

        }
    }
}
