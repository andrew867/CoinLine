using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCallPricingStructures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InitialChargeUsd",
                table: "Tariffs",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PricingMode",
                table: "Tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "InitialChargeUsd",
                table: "RateRules",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PricingMode",
                table: "RateRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialChargeUsd",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "PricingMode",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "InitialChargeUsd",
                table: "RateRules");

            migrationBuilder.DropColumn(
                name: "PricingMode",
                table: "RateRules");
        }
    }
}
