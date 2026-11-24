using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminalInmateConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TerminalInmateConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureConfigurationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SetupConfigurationId = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalInmateConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalInmateConfigurations_InmateFeatureConfigurations_Fe~",
                        column: x => x.FeatureConfigurationId,
                        principalTable: "InmateFeatureConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TerminalInmateConfigurations_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalInmateConfigurations_FeatureConfigurationId",
                table: "TerminalInmateConfigurations",
                column: "FeatureConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalInmateConfigurations_TerminalId",
                table: "TerminalInmateConfigurations",
                column: "TerminalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TerminalInmateConfigurations");
        }
    }
}
