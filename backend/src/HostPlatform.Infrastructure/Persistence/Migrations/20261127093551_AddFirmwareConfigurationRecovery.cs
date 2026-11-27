using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmwareConfigurationRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveredAtUtc",
                table: "FirmwareConfigurationTransitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecoveryBatchId",
                table: "FirmwareConfigurationTransitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecoveryObservationId",
                table: "FirmwareConfigurationTransitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_RecoveryBatchId",
                table: "FirmwareConfigurationTransitions",
                column: "RecoveryBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_RecoveryObservationId",
                table: "FirmwareConfigurationTransitions",
                column: "RecoveryObservationId");

            migrationBuilder.AddForeignKey(
                name: "FK_FirmwareConfigurationTransitions_DlogTransactions_RecoveryO~",
                table: "FirmwareConfigurationTransitions",
                column: "RecoveryObservationId",
                principalTable: "DlogTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FirmwareConfigurationTransitions_DownloadBatches_RecoveryBa~",
                table: "FirmwareConfigurationTransitions",
                column: "RecoveryBatchId",
                principalTable: "DownloadBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FirmwareConfigurationTransitions_DlogTransactions_RecoveryO~",
                table: "FirmwareConfigurationTransitions");

            migrationBuilder.DropForeignKey(
                name: "FK_FirmwareConfigurationTransitions_DownloadBatches_RecoveryBa~",
                table: "FirmwareConfigurationTransitions");

            migrationBuilder.DropIndex(
                name: "IX_FirmwareConfigurationTransitions_RecoveryBatchId",
                table: "FirmwareConfigurationTransitions");

            migrationBuilder.DropIndex(
                name: "IX_FirmwareConfigurationTransitions_RecoveryObservationId",
                table: "FirmwareConfigurationTransitions");

            migrationBuilder.DropColumn(
                name: "RecoveredAtUtc",
                table: "FirmwareConfigurationTransitions");

            migrationBuilder.DropColumn(
                name: "RecoveryBatchId",
                table: "FirmwareConfigurationTransitions");

            migrationBuilder.DropColumn(
                name: "RecoveryObservationId",
                table: "FirmwareConfigurationTransitions");
        }
    }
}
