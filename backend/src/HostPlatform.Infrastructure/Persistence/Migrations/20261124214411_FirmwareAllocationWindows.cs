using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmwareAllocationWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirmwareSlotEndAtUtc",
                table: "ScheduledTableDownloads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceTableSetId",
                table: "ScheduledTableDownloads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_SourceTableSetId",
                table: "ScheduledTableDownloads",
                column: "SourceTableSetId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledTableDownloads_TableSets_SourceTableSetId",
                table: "ScheduledTableDownloads",
                column: "SourceTableSetId",
                principalTable: "TableSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledTableDownloads_TableSets_SourceTableSetId",
                table: "ScheduledTableDownloads");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledTableDownloads_SourceTableSetId",
                table: "ScheduledTableDownloads");

            migrationBuilder.DropColumn(
                name: "FirmwareSlotEndAtUtc",
                table: "ScheduledTableDownloads");

            migrationBuilder.DropColumn(
                name: "SourceTableSetId",
                table: "ScheduledTableDownloads");
        }
    }
}
