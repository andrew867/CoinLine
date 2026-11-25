using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminalInventories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TerminalInventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Details_RegionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Details_Route = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_Stop = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_SiteDescription = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details_AddressLine1 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details_AddressLine2 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details_AddressLine3 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details_Contact = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Details_Indoor = table.Column<bool>(type: "boolean", nullable: false),
                    Details_Basement = table.Column<bool>(type: "boolean", nullable: false),
                    Details_ServiceClass = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_PublicService = table.Column<bool>(type: "boolean", nullable: false),
                    Details_SalespersonCode = table.Column<string>(type: "text", nullable: true),
                    Details_RevenueAccountingCode = table.Column<string>(type: "text", nullable: true),
                    Details_BillingCustomerCode = table.Column<string>(type: "text", nullable: true),
                    Details_VoucherAccountCode = table.Column<string>(type: "text", nullable: true),
                    Details_IndustryCode = table.Column<string>(type: "text", nullable: true),
                    Details_LocationGroupCode = table.Column<string>(type: "text", nullable: true),
                    Details_EquipmentCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Details_LoopMileageCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Details_HousingCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_DirectoryCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_DirectoryCount = table.Column<int>(type: "integer", nullable: true),
                    Details_SignCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_SignCount = table.Column<int>(type: "integer", nullable: true),
                    Details_ExtensionCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_ExtensionCount = table.Column<int>(type: "integer", nullable: true),
                    Details_MiscellaneousCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_MiscellaneousCount = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalInventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalInventories_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalInventories_TerminalId",
                table: "TerminalInventories",
                column: "TerminalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TerminalInventories");
        }
    }
}
