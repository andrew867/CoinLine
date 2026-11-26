using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCraftTechnicianAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical manual ingests may have retained a complete maintenance
            // record. Remove identifier bytes and any old raw-derived digest.
            migrationBuilder.Sql("""
                UPDATE "AuditEvents" AS audit SET "DetailJson" = '{"redacted":true}'
                WHERE audit."Category" = 'dlog' AND EXISTS (
                    SELECT 1 FROM "DlogTransactions" AS tx WHERE tx."MessageType" = 6
                    AND octet_length(tx."RawPayload") > 3 AND tx."Id"::text = audit."Resource");
                UPDATE "DlogParseDiagnostics" AS diagnostic
                SET "Message" = 'Legacy maintenance payload sanitized.', "Detail" = ''
                WHERE EXISTS (SELECT 1 FROM "DlogTransactions" AS tx WHERE tx."Id" = diagnostic."DlogTransactionId"
                    AND tx."MessageType" = 6 AND octet_length(tx."RawPayload") > 3);
                UPDATE "DlogTransactions" SET
                    "RawPayload" = CASE WHEN octet_length("RawPayload") >= 6 AND get_byte("RawPayload", 0) = 6
                        THEN substring("RawPayload" from 1 for 3) ELSE decode('06', 'hex') END,
                    "DecodedJson" = '{}',
                    "IdempotencyKey" = encode(sha256(convert_to('legacy-maintenance-redacted:' || "Id"::text, 'UTF8')), 'hex')
                WHERE "MessageType" = 6 AND octet_length("RawPayload") > 3;
                UPDATE "NccSessionFrames" SET "RawBytes" = decode('', 'hex')
                WHERE NOT "SentByHost" AND CASE WHEN octet_length("RawBytes") >= 11
                    THEN get_byte("RawBytes", 0) = 2 AND get_byte("RawBytes", 8) = 6 ELSE false END;
                UPDATE "NccSessions" SET "LastFrameSample" = decode('', 'hex')
                WHERE CASE WHEN octet_length("LastFrameSample") >= 11
                    THEN get_byte("LastFrameSample", 0) = 2 AND get_byte("LastFrameSample", 8) = 6 ELSE false END;
                """);
            migrationBuilder.CreateTable(
                name: "CraftTechnicians",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    EmployeeReference = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IdentifierKeyId = table.Column<string>(type: "text", nullable: true),
                    IdentifierFingerprint = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftTechnicians", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CraftMaintenanceAttributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicianId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayNameSnapshot = table.Column<string>(type: "text", nullable: true),
                    EmployeeReferenceSnapshot = table.Column<string>(type: "text", nullable: true),
                    MatchStatus = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftMaintenanceAttributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CraftMaintenanceAttributions_CraftTechnicians_TechnicianId",
                        column: x => x.TechnicianId,
                        principalTable: "CraftTechnicians",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CraftMaintenanceAttributions_DlogTransactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CraftMaintenanceAttributions_TechnicianId",
                table: "CraftMaintenanceAttributions",
                column: "TechnicianId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftMaintenanceAttributions_TransactionId",
                table: "CraftMaintenanceAttributions",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CraftTechnicians_IdentifierKeyId_IdentifierFingerprint",
                table: "CraftTechnicians",
                columns: new[] { "IdentifierKeyId", "IdentifierFingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CraftMaintenanceAttributions");

            migrationBuilder.DropTable(
                name: "CraftTechnicians");
        }
    }
}
