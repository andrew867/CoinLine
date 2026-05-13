using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NccSessionLifecycleStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "NccSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Historical rows: ended time implies Closed.
            migrationBuilder.Sql(
                """UPDATE "NccSessions" SET "Status" = 1 WHERE "EndedAtUtc" IS NOT NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "NccSessions");
        }
    }
}
