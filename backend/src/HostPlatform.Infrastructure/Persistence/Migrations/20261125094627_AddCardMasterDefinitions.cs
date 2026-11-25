using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCardMasterDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardMasterLinksJson",
                table: "TableVersions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CardMasterDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    CodeKey = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TableNumber = table.Column<int>(type: "integer", nullable: false),
                    RowPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardMasterDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardMasterDefinitions_TableNumber_CodeKey",
                table: "CardMasterDefinitions",
                columns: new[] { "TableNumber", "CodeKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardMasterDefinitions");

            migrationBuilder.DropColumn(
                name: "CardMasterLinksJson",
                table: "TableVersions");
        }
    }
}
