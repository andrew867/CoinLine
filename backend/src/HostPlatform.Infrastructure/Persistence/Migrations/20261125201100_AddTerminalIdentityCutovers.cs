using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    public partial class AddTerminalIdentityCutovers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TerminalIdentityCutoverItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivationError = table.Column<string>(type: "text", nullable: true),
                    ActivationSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplicationAcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    DispatchBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewIdentityObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewIdentityObservedSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PossiblyAppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceAccessRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccessRecordRevision = table.Column<int>(type: "integer", nullable: false),
                    SourceAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAssignmentRevision = table.Column<int>(type: "integer", nullable: false),
                    SourceFirmwareVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePayloadSha256Hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceTableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTableSetRevision = table.Column<int>(type: "integer", nullable: false),
                    SourceTerminalRevision = table.Column<int>(type: "integer", nullable: false),
                    SourceWireId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StagedTableSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetPayloadSha256Hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetWireId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferEndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransferError = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalIdentityCutoverItems", x => x.Id);
                    table.CheckConstraint("CK_TerminalIdentityCutoverItems_DecimalIds", "\"SourceWireId\" ~ '^[0-9]{10}$' AND \"TargetWireId\" ~ '^[0-9]{10}$' AND \"SourceWireId\" <> \"TargetWireId\"");
                    table.ForeignKey(
                        name: "FK_TerminalIdentityCutoverItems_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalIdentityCutoverItems_DueAtUtc",
                table: "TerminalIdentityCutoverItems",
                column: "DueAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalIdentityCutoverItems_JobId",
                table: "TerminalIdentityCutoverItems",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalIdentityCutoverItems_TargetWireId",
                table: "TerminalIdentityCutoverItems",
                column: "TargetWireId",
                unique: true,
                filter: "\"FinishedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalIdentityCutoverItems_TerminalId",
                table: "TerminalIdentityCutoverItems",
                column: "TerminalId",
                unique: true,
                filter: "\"FinishedAtUtc\" IS NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TerminalIdentityCutoverItems");
        }
    }
}
