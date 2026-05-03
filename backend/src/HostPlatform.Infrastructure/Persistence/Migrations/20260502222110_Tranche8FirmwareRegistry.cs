using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tranche8FirmwareRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StepStatus",
                table: "FirmwareUpdateSteps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "FirmwareUpdateJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByOperatorId",
                table: "FirmwareUpdateJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "FirmwareUpdateJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "FirmwareArtifactId",
                table: "FirmwareUpdateJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "FirmwareTargets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryArtifactId",
                table: "FirmwarePackages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FirmwareArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwarePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Sha256Hex = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageRef = table.Column<string>(type: "text", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareArtifacts_FirmwarePackages_FirmwarePackageId",
                        column: x => x.FirmwarePackageId,
                        principalTable: "FirmwarePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareCompatibilityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwarePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredTerminalFirmwareVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiredTargetSkuContains = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareCompatibilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareCompatibilityRules_FirmwarePackages_FirmwarePackage~",
                        column: x => x.FirmwarePackageId,
                        principalTable: "FirmwarePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FirmwareCompatibilityRules_FirmwareVersions_RequiredTermina~",
                        column: x => x.RequiredTerminalFirmwareVersionId,
                        principalTable: "FirmwareVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareRollBackPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwareUpdateJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    BackupNotes = table.Column<string>(type: "text", nullable: false),
                    RecoveryStepsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareRollBackPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareRollBackPlans_FirmwareUpdateJobs_FirmwareUpdateJobId",
                        column: x => x.FirmwareUpdateJobId,
                        principalTable: "FirmwareUpdateJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareUpdateSafetyChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwareUpdateJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareUpdateSafetyChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareUpdateSafetyChecks_FirmwareUpdateJobs_FirmwareUpdat~",
                        column: x => x.FirmwareUpdateJobId,
                        principalTable: "FirmwareUpdateJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareBlockManifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwarePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwareArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    LayoutJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareBlockManifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareBlockManifests_FirmwareArtifacts_FirmwareArtifactId",
                        column: x => x.FirmwareArtifactId,
                        principalTable: "FirmwareArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FirmwareBlockManifests_FirmwarePackages_FirmwarePackageId",
                        column: x => x.FirmwarePackageId,
                        principalTable: "FirmwarePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateJobs_FirmwareArtifactId",
                table: "FirmwareUpdateJobs",
                column: "FirmwareArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwarePackages_PrimaryArtifactId",
                table: "FirmwarePackages",
                column: "PrimaryArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareArtifacts_FirmwarePackageId",
                table: "FirmwareArtifacts",
                column: "FirmwarePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareBlockManifests_FirmwareArtifactId",
                table: "FirmwareBlockManifests",
                column: "FirmwareArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareBlockManifests_FirmwarePackageId",
                table: "FirmwareBlockManifests",
                column: "FirmwarePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareCompatibilityRules_FirmwarePackageId",
                table: "FirmwareCompatibilityRules",
                column: "FirmwarePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareCompatibilityRules_RequiredTerminalFirmwareVersionId",
                table: "FirmwareCompatibilityRules",
                column: "RequiredTerminalFirmwareVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareRollBackPlans_FirmwareUpdateJobId",
                table: "FirmwareRollBackPlans",
                column: "FirmwareUpdateJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateSafetyChecks_FirmwareUpdateJobId",
                table: "FirmwareUpdateSafetyChecks",
                column: "FirmwareUpdateJobId");

            migrationBuilder.AddForeignKey(
                name: "FK_FirmwarePackages_FirmwareArtifacts_PrimaryArtifactId",
                table: "FirmwarePackages",
                column: "PrimaryArtifactId",
                principalTable: "FirmwareArtifacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FirmwareUpdateJobs_FirmwareArtifacts_FirmwareArtifactId",
                table: "FirmwareUpdateJobs",
                column: "FirmwareArtifactId",
                principalTable: "FirmwareArtifacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FirmwarePackages_FirmwareArtifacts_PrimaryArtifactId",
                table: "FirmwarePackages");

            migrationBuilder.DropForeignKey(
                name: "FK_FirmwareUpdateJobs_FirmwareArtifacts_FirmwareArtifactId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropTable(
                name: "FirmwareBlockManifests");

            migrationBuilder.DropTable(
                name: "FirmwareCompatibilityRules");

            migrationBuilder.DropTable(
                name: "FirmwareRollBackPlans");

            migrationBuilder.DropTable(
                name: "FirmwareUpdateSafetyChecks");

            migrationBuilder.DropTable(
                name: "FirmwareArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_FirmwareUpdateJobs_FirmwareArtifactId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropIndex(
                name: "IX_FirmwarePackages_PrimaryArtifactId",
                table: "FirmwarePackages");

            migrationBuilder.DropColumn(
                name: "StepStatus",
                table: "FirmwareUpdateSteps");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "ApprovedByOperatorId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "FirmwareArtifactId",
                table: "FirmwareUpdateJobs");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "FirmwareTargets");

            migrationBuilder.DropColumn(
                name: "PrimaryArtifactId",
                table: "FirmwarePackages");
        }
    }
}
