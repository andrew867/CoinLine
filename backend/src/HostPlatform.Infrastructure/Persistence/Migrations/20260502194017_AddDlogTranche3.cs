using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDlogTranche3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "DlogTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "DlogTransactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "NccSessionId",
                table: "DlogTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingStatus",
                table: "DlogTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResultSummaryJson",
                table: "DlogReplayRequests",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DlogCorrelationLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkRule = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlogCorrelationLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DlogCorrelationLinks_DlogTransactions_RequestTransactionId",
                        column: x => x.RequestTransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DlogCorrelationLinks_DlogTransactions_ResponseTransactionId",
                        column: x => x.ResponseTransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DlogMessageTypes",
                columns: table => new
                {
                    MtCode = table.Column<int>(type: "integer", nullable: false),
                    SymbolName = table.Column<string>(type: "text", nullable: false),
                    MessageAction = table.Column<string>(type: "text", nullable: false),
                    ImmediateClear = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlogMessageTypes", x => x.MtCode);
                });

            migrationBuilder.CreateTable(
                name: "DlogParseDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DlogTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlogParseDiagnostics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DlogParseDiagnostics_DlogTransactions_DlogTransactionId",
                        column: x => x.DlogTransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DlogTransactions_IdempotencyKey",
                table: "DlogTransactions",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL AND \"IdempotencyKey\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DlogTransactions_NccSessionId",
                table: "DlogTransactions",
                column: "NccSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DlogCorrelationLinks_RequestTransactionId",
                table: "DlogCorrelationLinks",
                column: "RequestTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_DlogCorrelationLinks_ResponseTransactionId",
                table: "DlogCorrelationLinks",
                column: "ResponseTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_DlogParseDiagnostics_DlogTransactionId",
                table: "DlogParseDiagnostics",
                column: "DlogTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_DlogTransactions_NccSessions_NccSessionId",
                table: "DlogTransactions",
                column: "NccSessionId",
                principalTable: "NccSessions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DlogTransactions_NccSessions_NccSessionId",
                table: "DlogTransactions");

            migrationBuilder.DropTable(
                name: "DlogCorrelationLinks");

            migrationBuilder.DropTable(
                name: "DlogMessageTypes");

            migrationBuilder.DropTable(
                name: "DlogParseDiagnostics");

            migrationBuilder.DropIndex(
                name: "IX_DlogTransactions_IdempotencyKey",
                table: "DlogTransactions");

            migrationBuilder.DropIndex(
                name: "IX_DlogTransactions_NccSessionId",
                table: "DlogTransactions");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "DlogTransactions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "DlogTransactions");

            migrationBuilder.DropColumn(
                name: "NccSessionId",
                table: "DlogTransactions");

            migrationBuilder.DropColumn(
                name: "ProcessingStatus",
                table: "DlogTransactions");

            migrationBuilder.DropColumn(
                name: "ResultSummaryJson",
                table: "DlogReplayRequests");
        }
    }
}
