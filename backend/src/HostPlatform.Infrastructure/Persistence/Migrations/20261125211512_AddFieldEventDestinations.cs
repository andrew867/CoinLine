using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldEventDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldEventDestinationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: true),
                    RegionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Destination = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldEventDestinationRules", x => x.Id);
                    table.CheckConstraint("CK_FieldEventDestinationRules_KindCode", "(\"Kind\" = 'alarm' AND (\"Code\" IS NULL OR \"Code\" BETWEEN 0 AND 255)) OR (\"Kind\" = 'maintenance' AND (\"Code\" IS NULL OR \"Code\" BETWEEN 0 AND 65535))");
                });

            migrationBuilder.CreateTable(
                name: "FieldEventRouteDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Destination = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RuleIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldEventRouteDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldEventRouteDecisions_DlogTransactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldEventDestinationRules_ScopeKey",
                table: "FieldEventDestinationRules",
                column: "ScopeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldEventRouteDecisions_TransactionId",
                table: "FieldEventRouteDecisions",
                column: "TransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldEventDestinationRules");

            migrationBuilder.DropTable(
                name: "FieldEventRouteDecisions");
        }
    }
}
