using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tranche5RatingAndCallRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RateRules_RatePlans_RatePlanId",
                table: "RateRules");

            migrationBuilder.DropIndex(
                name: "IX_RateRules_RatePlanId",
                table: "RateRules");

            migrationBuilder.CreateTable(
                name: "RatePlanVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatePlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatePlanVersions_RatePlans_RatePlanId",
                        column: x => x.RatePlanId,
                        principalTable: "RatePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "RatePlanVersions" ("Id","RatePlanId","VersionNumber","Status","PublishedAtUtc","CreatedAtUtc","UpdatedAtUtc","CreatedBy","UpdatedBy","Version")
                SELECT gen_random_uuid(), rp."Id", 1, 0, NULL, NOW() AT TIME ZONE 'UTC', NOW() AT TIME ZONE 'UTC', 'migration', 'migration', 0
                FROM "RatePlans" rp;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "RatePlanVersionId",
                table: "RateRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "RateRules" rr
                SET "RatePlanVersionId" = v."Id"
                FROM "RatePlanVersions" v
                WHERE v."RatePlanId" = rr."RatePlanId" AND v."VersionNumber" = 1;
                """);

            migrationBuilder.Sql("""DELETE FROM "RateRules" WHERE "RatePlanVersionId" IS NULL;""");

            migrationBuilder.DropColumn(
                name: "RatePlanId",
                table: "RateRules");

            migrationBuilder.AlterColumn<Guid>(
                name: "RatePlanVersionId",
                table: "RateRules",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DecisionKind",
                table: "RatingResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeterminismInputJson",
                table: "RatingResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "RatePlanVersionId",
                table: "RatingResults",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatchKind",
                table: "RateRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Outcome",
                table: "RateRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "RateRules",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "RateRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "RatePerMinuteUsd",
                table: "RateRules",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishedVersionId",
                table: "RatePlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "DialedNumberClasses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "DialedNumberClasses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmergency",
                table: "DialedNumberClasses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFree",
                table: "DialedNumberClasses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MatchKind",
                table: "DialedNumberClasses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "DialedNumberClasses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "AppliedRatePlanVersionId",
                table: "CallRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Reconciliation",
                table: "CallRecords",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CallAuthorizationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    RatePlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    DialedDigits = table.Column<string>(type: "text", nullable: false),
                    AvailableBalanceUsd = table.Column<decimal>(type: "numeric", nullable: true),
                    RequestPayloadJson = table.Column<string>(type: "text", nullable: false),
                    DecisionPayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallAuthorizationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallAuthorizationRequests_RatePlans_RatePlanId",
                        column: x => x.RatePlanId,
                        principalTable: "RatePlans",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CallAuthorizationRequests_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CallChargeSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatingResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentIndex = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    AmountUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallChargeSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallChargeSegments_RatingResults_RatingResultId",
                        column: x => x.RatingResultId,
                        principalTable: "RatingResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RatingDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatingResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingDiagnostics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatingDiagnostics_RatingResults_RatingResultId",
                        column: x => x.RatingResultId,
                        principalTable: "RatingResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tariffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    RatePerMinuteUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tariffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tariffs_RatePlanVersions_RatePlanVersionId",
                        column: x => x.RatePlanVersionId,
                        principalTable: "RatePlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DestinationPrefixes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrefixDigits = table.Column<string>(type: "text", nullable: false),
                    TariffId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationPrefixes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DestinationPrefixes_RatePlanVersions_RatePlanVersionId",
                        column: x => x.RatePlanVersionId,
                        principalTable: "RatePlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationPrefixes_Tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "Tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TimeBands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeekMask = table.Column<int>(type: "integer", nullable: false),
                    StartMinuteOfDay = table.Column<int>(type: "integer", nullable: false),
                    EndMinuteOfDay = table.Column<int>(type: "integer", nullable: false),
                    TariffId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeBands_RatePlanVersions_RatePlanVersionId",
                        column: x => x.RatePlanVersionId,
                        principalTable: "RatePlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimeBands_Tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "Tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RatingResults_RatePlanVersionId",
                table: "RatingResults",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RatePlans_PublishedVersionId",
                table: "RatePlans",
                column: "PublishedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DialedNumberClasses_CustomerId",
                table: "DialedNumberClasses",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_AppliedRatePlanVersionId",
                table: "CallRecords",
                column: "AppliedRatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CallAuthorizationRequests_RatePlanId",
                table: "CallAuthorizationRequests",
                column: "RatePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CallAuthorizationRequests_TerminalId",
                table: "CallAuthorizationRequests",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_CallChargeSegments_RatingResultId",
                table: "CallChargeSegments",
                column: "RatingResultId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationPrefixes_RatePlanVersionId",
                table: "DestinationPrefixes",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationPrefixes_TariffId",
                table: "DestinationPrefixes",
                column: "TariffId");

            migrationBuilder.CreateIndex(
                name: "IX_RatePlanVersions_RatePlanId_VersionNumber",
                table: "RatePlanVersions",
                columns: new[] { "RatePlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RateRules_RatePlanVersionId",
                table: "RateRules",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RatingDiagnostics_RatingResultId",
                table: "RatingDiagnostics",
                column: "RatingResultId");

            migrationBuilder.CreateIndex(
                name: "IX_Tariffs_RatePlanVersionId",
                table: "Tariffs",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeBands_RatePlanVersionId",
                table: "TimeBands",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeBands_TariffId",
                table: "TimeBands",
                column: "TariffId");

            migrationBuilder.AddForeignKey(
                name: "FK_CallRecords_RatePlanVersions_AppliedRatePlanVersionId",
                table: "CallRecords",
                column: "AppliedRatePlanVersionId",
                principalTable: "RatePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DialedNumberClasses_Customers_CustomerId",
                table: "DialedNumberClasses",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RatePlans_RatePlanVersions_PublishedVersionId",
                table: "RatePlans",
                column: "PublishedVersionId",
                principalTable: "RatePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RateRules_RatePlanVersions_RatePlanVersionId",
                table: "RateRules",
                column: "RatePlanVersionId",
                principalTable: "RatePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RatingResults_RatePlanVersions_RatePlanVersionId",
                table: "RatingResults",
                column: "RatePlanVersionId",
                principalTable: "RatePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CallRecords_RatePlanVersions_AppliedRatePlanVersionId",
                table: "CallRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_DialedNumberClasses_Customers_CustomerId",
                table: "DialedNumberClasses");

            migrationBuilder.DropForeignKey(
                name: "FK_RatePlans_RatePlanVersions_PublishedVersionId",
                table: "RatePlans");

            migrationBuilder.DropForeignKey(
                name: "FK_RateRules_RatePlanVersions_RatePlanVersionId",
                table: "RateRules");

            migrationBuilder.DropForeignKey(
                name: "FK_RatingResults_RatePlanVersions_RatePlanVersionId",
                table: "RatingResults");

            migrationBuilder.DropTable(
                name: "CallAuthorizationRequests");

            migrationBuilder.DropTable(
                name: "CallChargeSegments");

            migrationBuilder.DropTable(
                name: "DestinationPrefixes");

            migrationBuilder.DropTable(
                name: "RatingDiagnostics");

            migrationBuilder.DropTable(
                name: "TimeBands");

            migrationBuilder.DropTable(
                name: "Tariffs");

            migrationBuilder.AddColumn<Guid>(
                name: "RatePlanId",
                table: "RateRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "RateRules" rr
                SET "RatePlanId" = v."RatePlanId"
                FROM "RatePlanVersions" v
                WHERE v."Id" = rr."RatePlanVersionId";
                """);

            migrationBuilder.Sql("""DELETE FROM "RateRules" WHERE "RatePlanId" IS NULL;""");

            migrationBuilder.AlterColumn<Guid>(
                name: "RatePlanId",
                table: "RateRules",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "RatePlanVersionId",
                table: "RateRules");

            migrationBuilder.DropTable(
                name: "RatePlanVersions");

            migrationBuilder.DropIndex(
                name: "IX_RatingResults_RatePlanVersionId",
                table: "RatingResults");

            migrationBuilder.DropIndex(
                name: "IX_RatePlans_PublishedVersionId",
                table: "RatePlans");

            migrationBuilder.DropIndex(
                name: "IX_DialedNumberClasses_CustomerId",
                table: "DialedNumberClasses");

            migrationBuilder.DropIndex(
                name: "IX_CallRecords_AppliedRatePlanVersionId",
                table: "CallRecords");

            migrationBuilder.DropIndex(
                name: "IX_RateRules_RatePlanVersionId",
                table: "RateRules");

            migrationBuilder.DropColumn(
                name: "DecisionKind",
                table: "RatingResults");

            migrationBuilder.DropColumn(
                name: "DeterminismInputJson",
                table: "RatingResults");

            migrationBuilder.DropColumn(
                name: "RatePlanVersionId",
                table: "RatingResults");

            migrationBuilder.DropColumn(
                name: "MatchKind",
                table: "RateRules");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "RateRules");

            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "RateRules");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "RateRules");

            migrationBuilder.DropColumn(
                name: "RatePerMinuteUsd",
                table: "RateRules");

            migrationBuilder.DropColumn(
                name: "PublishedVersionId",
                table: "RatePlans");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "DialedNumberClasses");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "DialedNumberClasses");

            migrationBuilder.DropColumn(
                name: "IsEmergency",
                table: "DialedNumberClasses");

            migrationBuilder.DropColumn(
                name: "IsFree",
                table: "DialedNumberClasses");

            migrationBuilder.DropColumn(
                name: "MatchKind",
                table: "DialedNumberClasses");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "DialedNumberClasses");

            migrationBuilder.DropColumn(
                name: "AppliedRatePlanVersionId",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "Reconciliation",
                table: "CallRecords");

            migrationBuilder.CreateIndex(
                name: "IX_RateRules_RatePlanId",
                table: "RateRules",
                column: "RatePlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_RateRules_RatePlans_RatePlanId",
                table: "RateRules",
                column: "RatePlanId",
                principalTable: "RatePlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
