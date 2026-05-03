using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tranche6CardPaymentsSkeleton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SmartcardTypes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MapsToCardType",
                table: "SmartcardTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "SmartcardTypes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawPayloadJson",
                table: "PaymentTransactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ReportedCardType",
                table: "PaymentTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AllowNegativeBalance",
                table: "CardProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DefaultCardType",
                table: "CardProducts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsTestFixtureCatalogEntry",
                table: "CardProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CredentialKind",
                table: "CardAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CredentialTokenRef",
                table: "CardAccounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ResolvedCardType",
                table: "CardAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CardBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardBalances_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenReference = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardCredentials_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardReadEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportedCardType = table.Column<int>(type: "integer", nullable: false),
                    RawPayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardReadEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardReadEvents_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CardReadEvents_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CardReconciliationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardReconciliationBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardWriteEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    IntendedOperation = table.Column<string>(type: "text", nullable: false),
                    RawPayloadJson = table.Column<string>(type: "text", nullable: false),
                    Disposition = table.Column<int>(type: "integer", nullable: false),
                    SimulationMode = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardWriteEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardWriteEvents_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EPurseProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EPurseProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EPurseProfiles_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmartcardProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SmartcardTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartcardProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartcardProfiles_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SmartcardProfiles_SmartcardTypes_SmartcardTypeId",
                        column: x => x.SmartcardTypeId,
                        principalTable: "SmartcardTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardBalances_CardAccountId",
                table: "CardBalances",
                column: "CardAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardCredentials_CardAccountId",
                table: "CardCredentials",
                column: "CardAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CardReadEvents_CardAccountId",
                table: "CardReadEvents",
                column: "CardAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CardReadEvents_TerminalId",
                table: "CardReadEvents",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWriteEvents_CardAccountId",
                table: "CardWriteEvents",
                column: "CardAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EPurseProfiles_CardAccountId",
                table: "EPurseProfiles",
                column: "CardAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartcardProfiles_CardAccountId",
                table: "SmartcardProfiles",
                column: "CardAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartcardProfiles_SmartcardTypeId",
                table: "SmartcardProfiles",
                column: "SmartcardTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardBalances");

            migrationBuilder.DropTable(
                name: "CardCredentials");

            migrationBuilder.DropTable(
                name: "CardReadEvents");

            migrationBuilder.DropTable(
                name: "CardReconciliationBatches");

            migrationBuilder.DropTable(
                name: "CardWriteEvents");

            migrationBuilder.DropTable(
                name: "EPurseProfiles");

            migrationBuilder.DropTable(
                name: "SmartcardProfiles");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "SmartcardTypes");

            migrationBuilder.DropColumn(
                name: "MapsToCardType",
                table: "SmartcardTypes");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "SmartcardTypes");

            migrationBuilder.DropColumn(
                name: "RawPayloadJson",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "ReportedCardType",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "AllowNegativeBalance",
                table: "CardProducts");

            migrationBuilder.DropColumn(
                name: "DefaultCardType",
                table: "CardProducts");

            migrationBuilder.DropColumn(
                name: "IsTestFixtureCatalogEntry",
                table: "CardProducts");

            migrationBuilder.DropColumn(
                name: "CredentialKind",
                table: "CardAccounts");

            migrationBuilder.DropColumn(
                name: "CredentialTokenRef",
                table: "CardAccounts");

            migrationBuilder.DropColumn(
                name: "ResolvedCardType",
                table: "CardAccounts");
        }
    }
}
