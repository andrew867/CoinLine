using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldEventCodeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldEventCodeSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldEventCodeSettings", x => x.Id);
                    table.CheckConstraint("CK_FieldEventCodeSettings_KindCodeSeverity", "(\"Kind\" = 'alarm' AND \"Code\" BETWEEN 0 AND 255 AND (\"Severity\" IS NULL OR \"Severity\" BETWEEN 1 AND 4)) OR (\"Kind\" = 'maintenance' AND \"Code\" BETWEEN 0 AND 65535 AND \"Severity\" IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldEventCodeSettings_Kind_Code",
                table: "FieldEventCodeSettings",
                columns: new[] { "Kind", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldEventCodeSettings");
        }
    }
}
