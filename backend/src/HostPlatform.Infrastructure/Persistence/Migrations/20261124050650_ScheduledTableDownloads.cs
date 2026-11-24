using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledTableDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledTableDownloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NccSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTableDownloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_DownloadBatches_LastBatchId",
                        column: x => x.LastBatchId,
                        principalTable: "DownloadBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_NccSessions_NccSessionId",
                        column: x => x.NccSessionId,
                        principalTable: "NccSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_TableSets_TableSetId",
                        column: x => x.TableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_LastBatchId",
                table: "ScheduledTableDownloads",
                column: "LastBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_NccSessionId",
                table: "ScheduledTableDownloads",
                column: "NccSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_TableSetId_AvailableAtUtc",
                table: "ScheduledTableDownloads",
                columns: new[] { "TableSetId", "AvailableAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_TerminalId",
                table: "ScheduledTableDownloads",
                column: "TerminalId",
                unique: true,
                filter: "\"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledTableDownloads");
        }
    }
}
