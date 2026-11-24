using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TableConfigurationIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigurationId",
                table: "TableVersions",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TableVersions_ConfigurationId_TableDefinitionId",
                table: "TableVersions",
                columns: new[] { "ConfigurationId", "TableDefinitionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TableVersions_ConfigurationId_TableDefinitionId",
                table: "TableVersions");

            migrationBuilder.DropColumn(
                name: "ConfigurationId",
                table: "TableVersions");
        }
    }
}
