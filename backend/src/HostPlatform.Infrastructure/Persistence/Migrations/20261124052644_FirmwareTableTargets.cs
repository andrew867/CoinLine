using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmwareTableTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetFirmwareVersionId",
                table: "TableSets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TableProfileId",
                table: "FirmwareVersions",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TableSets_TargetFirmwareVersionId",
                table: "TableSets",
                column: "TargetFirmwareVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_TableSets_FirmwareVersions_TargetFirmwareVersionId",
                table: "TableSets",
                column: "TargetFirmwareVersionId",
                principalTable: "FirmwareVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TableSets_FirmwareVersions_TargetFirmwareVersionId",
                table: "TableSets");

            migrationBuilder.DropIndex(
                name: "IX_TableSets_TargetFirmwareVersionId",
                table: "TableSets");

            migrationBuilder.DropColumn(
                name: "TargetFirmwareVersionId",
                table: "TableSets");

            migrationBuilder.DropColumn(
                name: "TableProfileId",
                table: "FirmwareVersions");
        }
    }
}
