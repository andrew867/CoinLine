using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantCaptureIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MerchantCaptureIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizationRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CallSequence = table.Column<int>(type: "integer", nullable: false),
                    CallStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    AmountUsd = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    BindingFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastResult = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantCaptureIntents", x => x.Id);
                    table.CheckConstraint("CK_MerchantCaptureIntent_CallSequence", "\"CallSequence\" BETWEEN 0 AND 65535");
                    table.CheckConstraint("CK_MerchantCaptureIntent_Duration", "\"DurationSeconds\" > 0");
                    table.CheckConstraint("CK_MerchantCaptureIntent_PositiveAmount", "\"AmountUsd\" > 0 AND \"AmountUsd\" <= 655.35");
                    table.CheckConstraint("CK_MerchantCaptureIntent_State", "\"AttemptCount\" >= 0 AND \"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_MerchantCaptureIntents_AuditEvents_AuthorizationDecisionId",
                        column: x => x.AuthorizationDecisionId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MerchantCaptureIntents_AuditEvents_AuthorizationRequestId",
                        column: x => x.AuthorizationRequestId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MerchantCaptureIntents_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MerchantCaptureIntents_UploadBatches_UploadBatchId",
                        column: x => x.UploadBatchId,
                        principalTable: "UploadBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCaptureIntents_AuthorizationDecisionId",
                table: "MerchantCaptureIntents",
                column: "AuthorizationDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCaptureIntents_AuthorizationRequestId",
                table: "MerchantCaptureIntents",
                column: "AuthorizationRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCaptureIntents_Status_NextAttemptAtUtc",
                table: "MerchantCaptureIntents",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCaptureIntents_TerminalId",
                table: "MerchantCaptureIntents",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantCaptureIntents_UploadBatchId",
                table: "MerchantCaptureIntents",
                column: "UploadBatchId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantCaptureIntents");
        }
    }
}
