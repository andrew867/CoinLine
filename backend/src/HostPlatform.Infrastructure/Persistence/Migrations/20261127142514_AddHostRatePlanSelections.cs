using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHostRatePlanSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostRatePlanSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    RatePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostRatePlanSelections", x => x.Id);
                    table.CheckConstraint("CK_HostRatePlanSelections_ExactlyOneScope", "(\"CustomerId\" IS NULL) <> (\"TerminalId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_HostRatePlanSelections_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostRatePlanSelections_RatePlans_RatePlanId",
                        column: x => x.RatePlanId,
                        principalTable: "RatePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostRatePlanSelections_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostRatePlanSelections_CustomerId",
                table: "HostRatePlanSelections",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostRatePlanSelections_RatePlanId",
                table: "HostRatePlanSelections",
                column: "RatePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_HostRatePlanSelections_TerminalId",
                table: "HostRatePlanSelections",
                column: "TerminalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostRatePlanSelections");
        }
    }
}
