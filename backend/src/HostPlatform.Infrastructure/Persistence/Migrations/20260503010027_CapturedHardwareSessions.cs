using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CapturedHardwareSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CapturedHardwareSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    SessionKind = table.Column<string>(type: "text", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceLabel = table.Column<string>(type: "text", nullable: false),
                    EnvelopeJson = table.Column<string>(type: "text", nullable: false),
                    EnvelopeChecksumSha256 = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapturedHardwareSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapturedHardwareSessions_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapturedHardwareSessions_CreatedAtUtc",
                table: "CapturedHardwareSessions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CapturedHardwareSessions_EnvelopeChecksumSha256",
                table: "CapturedHardwareSessions",
                column: "EnvelopeChecksumSha256");

            migrationBuilder.CreateIndex(
                name: "IX_CapturedHardwareSessions_SessionKind",
                table: "CapturedHardwareSessions",
                column: "SessionKind");

            migrationBuilder.CreateIndex(
                name: "IX_CapturedHardwareSessions_TerminalId",
                table: "CapturedHardwareSessions",
                column: "TerminalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapturedHardwareSessions");
        }
    }
}
