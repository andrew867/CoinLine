using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NccSessionFramesAndCallPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RequestRaw",
                table: "CallRecords",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "ResponseRaw",
                table: "CallRecords",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "NccSessionFrames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NccSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TerminalIdHex = table.Column<string>(type: "text", nullable: false),
                    TerminalMatched = table.Column<bool>(type: "boolean", nullable: false),
                    RawBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NccSessionFrames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NccSessionFrames_NccSessions_NccSessionId",
                        column: x => x.NccSessionId,
                        principalTable: "NccSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NccSessionFrames_NccSessionId_Sequence",
                table: "NccSessionFrames",
                columns: new[] { "NccSessionId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NccSessionFrames");

            migrationBuilder.DropColumn(
                name: "RequestRaw",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "ResponseRaw",
                table: "CallRecords");
        }
    }
}
