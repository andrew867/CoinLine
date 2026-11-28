using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeJobScheduleBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedArtifactSha256Hex",
                table: "FirmwareUpdateJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ApprovedArtifactSizeBytes",
                table: "FirmwareUpdateJobs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CodeScheduleId",
                table: "FirmwareUpdateJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CodeTransitionId",
                table: "FirmwareUpdateJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetFirmwareVersionId",
                table: "FirmwarePackages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateJobs_CodeScheduleId",
                table: "FirmwareUpdateJobs",
                column: "CodeScheduleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateJobs_CodeTransitionId",
                table: "FirmwareUpdateJobs",
                column: "CodeTransitionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmwarePackages_TargetFirmwareVersionId",
                table: "FirmwarePackages",
                column: "TargetFirmwareVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_FirmwarePackages_FirmwareVersions_TargetFirmwareVersionId",
                table: "FirmwarePackages",
                column: "TargetFirmwareVersionId",
                principalTable: "FirmwareVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FirmwareUpdateJobs_FirmwareConfigurationTransitions_CodeTra~",
                table: "FirmwareUpdateJobs",
                column: "CodeTransitionId",
                principalTable: "FirmwareConfigurationTransitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FirmwareUpdateJobs_ScheduledTableDownloads_CodeScheduleId",
                table: "FirmwareUpdateJobs",
                column: "CodeScheduleId",
                principalTable: "ScheduledTableDownloads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FirmwarePackages_FirmwareVersions_TargetFirmwareVersionId",
                table: "FirmwarePackages");

            migrationBuilder.DropForeignKey(
                name: "FK_FirmwareUpdateJobs_FirmwareConfigurationTransitions_CodeTra~",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_FirmwareUpdateJobs_ScheduledTableDownloads_CodeScheduleId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropIndex(
                name: "IX_FirmwareUpdateJobs_CodeScheduleId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropIndex(
                name: "IX_FirmwareUpdateJobs_CodeTransitionId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropIndex(
                name: "IX_FirmwarePackages_TargetFirmwareVersionId",
                table: "FirmwarePackages");

            migrationBuilder.DropColumn(
                name: "ApprovedArtifactSha256Hex",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "ApprovedArtifactSizeBytes",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "CodeScheduleId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "CodeTransitionId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "TargetFirmwareVersionId",
                table: "FirmwarePackages");
        }
    }
}
