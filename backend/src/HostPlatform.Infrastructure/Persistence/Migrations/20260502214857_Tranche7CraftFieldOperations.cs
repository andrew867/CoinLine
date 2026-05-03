using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tranche7CraftFieldOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldNotes",
                table: "CraftSessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AuditReason",
                table: "CraftCommands",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "CraftCommandTypeId",
                table: "CraftCommands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DestructiveConfirmed",
                table: "CraftCommands",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SimulationExecution",
                table: "CraftCommands",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "CdrUploadRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SimulationMode = table.Column<bool>(type: "boolean", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    CraftSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CdrUploadRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CdrUploadRequests_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CraftCommandTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    IsDestructive = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultSimulationOnly = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftCommandTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CraftDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CraftSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftDiagnostics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CraftDiagnostics_CraftSessions_CraftSessionId",
                        column: x => x.CraftSessionId,
                        principalTable: "CraftSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CraftDiagnostics_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemoteTableReloadRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SimulationMode = table.Column<bool>(type: "boolean", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    CraftSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteTableReloadRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemoteTableReloadRequests_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalDiagnosticSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalDiagnosticSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalDiagnosticSnapshots_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CraftCommands_CraftCommandTypeId",
                table: "CraftCommands",
                column: "CraftCommandTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CdrUploadRequests_TerminalId",
                table: "CdrUploadRequests",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftCommandTypes_Code",
                table: "CraftCommandTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CraftDiagnostics_CraftSessionId",
                table: "CraftDiagnostics",
                column: "CraftSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftDiagnostics_TerminalId",
                table: "CraftDiagnostics",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteTableReloadRequests_TerminalId",
                table: "RemoteTableReloadRequests",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalDiagnosticSnapshots_TerminalId",
                table: "TerminalDiagnosticSnapshots",
                column: "TerminalId");

            migrationBuilder.AddForeignKey(
                name: "FK_CraftCommands_CraftCommandTypes_CraftCommandTypeId",
                table: "CraftCommands",
                column: "CraftCommandTypeId",
                principalTable: "CraftCommandTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CraftCommands_CraftCommandTypes_CraftCommandTypeId",
                table: "CraftCommands");

            migrationBuilder.DropTable(
                name: "CdrUploadRequests");

            migrationBuilder.DropTable(
                name: "CraftCommandTypes");

            migrationBuilder.DropTable(
                name: "CraftDiagnostics");

            migrationBuilder.DropTable(
                name: "RemoteTableReloadRequests");

            migrationBuilder.DropTable(
                name: "TerminalDiagnosticSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_CraftCommands_CraftCommandTypeId",
                table: "CraftCommands");

            migrationBuilder.DropColumn(
                name: "FieldNotes",
                table: "CraftSessions");

            migrationBuilder.DropColumn(
                name: "AuditReason",
                table: "CraftCommands");

            migrationBuilder.DropColumn(
                name: "CraftCommandTypeId",
                table: "CraftCommands");

            migrationBuilder.DropColumn(
                name: "DestructiveConfirmed",
                table: "CraftCommands");

            migrationBuilder.DropColumn(
                name: "SimulationExecution",
                table: "CraftCommands");
        }
    }
}
