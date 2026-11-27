using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmwareConfigurationTransition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FirmwareConfigurationTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeRecordVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeRecordSha256Hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceFirmwareVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetFirmwareVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAssignmentRevision = table.Column<int>(type: "integer", nullable: false),
                    SourceTableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceControllerRom = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    SourceControllerSoftware = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    TargetControllerRom = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    TargetControllerSoftware = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    TargetObservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareConfigurationTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_DlogTransactions_SourceObs~",
                        column: x => x.SourceObservationId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_DlogTransactions_TargetObs~",
                        column: x => x.TargetObservationId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_DownloadBatches_TargetBatc~",
                        column: x => x.TargetBatchId,
                        principalTable: "DownloadBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_FirmwareVersions_SourceFir~",
                        column: x => x.SourceFirmwareVersionId,
                        principalTable: "FirmwareVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_FirmwareVersions_TargetFir~",
                        column: x => x.TargetFirmwareVersionId,
                        principalTable: "FirmwareVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_ScheduledTableDownloads_Co~",
                        column: x => x.CodeScheduleId,
                        principalTable: "ScheduledTableDownloads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_TableSets_SourceTableSetId",
                        column: x => x.SourceTableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_TableSets_TargetTableSetId",
                        column: x => x.TargetTableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_TableVersions_CodeRecordVe~",
                        column: x => x.CodeRecordVersionId,
                        principalTable: "TableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationTransitions_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_CodeRecordVersionId",
                table: "FirmwareConfigurationTransitions",
                column: "CodeRecordVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_CodeScheduleId",
                table: "FirmwareConfigurationTransitions",
                column: "CodeScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_SourceFirmwareVersionId",
                table: "FirmwareConfigurationTransitions",
                column: "SourceFirmwareVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_SourceObservationId",
                table: "FirmwareConfigurationTransitions",
                column: "SourceObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_SourceTableSetId",
                table: "FirmwareConfigurationTransitions",
                column: "SourceTableSetId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_TargetBatchId",
                table: "FirmwareConfigurationTransitions",
                column: "TargetBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_TargetFirmwareVersionId",
                table: "FirmwareConfigurationTransitions",
                column: "TargetFirmwareVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_TargetObservationId",
                table: "FirmwareConfigurationTransitions",
                column: "TargetObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_TargetTableSetId",
                table: "FirmwareConfigurationTransitions",
                column: "TargetTableSetId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationTransitions_TerminalId",
                table: "FirmwareConfigurationTransitions",
                column: "TerminalId",
                unique: true,
                filter: "\"ActivatedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FirmwareConfigurationTransitions");
        }
    }
}
