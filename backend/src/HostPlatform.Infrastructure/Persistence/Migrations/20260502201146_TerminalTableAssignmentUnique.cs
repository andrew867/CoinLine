using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminalTableAssignmentUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TerminalTableAssignments_TerminalId",
                table: "TerminalTableAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableAssignments_TerminalId",
                table: "TerminalTableAssignments",
                column: "TerminalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TerminalTableAssignments_TerminalId",
                table: "TerminalTableAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableAssignments_TerminalId",
                table: "TerminalTableAssignments",
                column: "TerminalId");
        }
    }
}
