using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MinimizeCardEventData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Discard opaque legacy input before removing its columns. Never parse or copy it.
            migrationBuilder.Sql("""
                UPDATE "CardReadEvents" SET "RawPayloadJson" = '{}';
                UPDATE "CardWriteEvents" SET "RawPayloadJson" = '{}', "IntendedOperation" = '';
                UPDATE "CardAccounts" SET "PanLast4" = '' WHERE "PanLast4" !~ '^[0-9]{4}$';
                UPDATE "AuditEvents" SET "DetailJson" = '{"redacted":true}'
                  WHERE "Category" = 'cards' AND "Action" IN ('card_read_event', 'card_write_simulated');
                UPDATE "PaymentTransactions" AS payment
                  SET "RawPayloadJson" = '{}',
                      "DetailJson" = CASE WHEN EXISTS (
                        SELECT 1 FROM "AuditEvents" AS audit
                        WHERE audit."Category" = 'cards' AND audit."Action" = 'synthetic_cdr_settlement'
                          AND audit."Resource" = payment."Id"::text AND audit."DetailJson" = payment."DetailJson"
                      ) THEN payment."DetailJson" ELSE '{}' END;
                """);

            migrationBuilder.DropColumn(
                name: "IntendedOperation",
                table: "CardWriteEvents");

            migrationBuilder.DropColumn(
                name: "RawPayloadJson",
                table: "CardWriteEvents");

            migrationBuilder.DropColumn(
                name: "RawPayloadJson",
                table: "CardReadEvents");

            migrationBuilder.AddColumn<string>(
                name: "PanLast4",
                table: "CardWriteEvents",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportedCardType",
                table: "CardWriteEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PanLast4",
                table: "CardReadEvents",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Result",
                table: "CardReadEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Only the existing, independently validated account fragment may be retained.
            migrationBuilder.Sql("""
                UPDATE "CardReadEvents" AS event SET "PanLast4" = account."PanLast4"
                  FROM "CardAccounts" AS account WHERE event."CardAccountId" = account."Id"
                    AND account."PanLast4" ~ '^[0-9]{4}$';
                UPDATE "CardWriteEvents" AS event SET "PanLast4" = account."PanLast4",
                  "ReportedCardType" = CASE WHEN account."ResolvedCardType" BETWEEN 0 AND 6 THEN account."ResolvedCardType" ELSE 0 END
                  FROM "CardAccounts" AS account WHERE event."CardAccountId" = account."Id"
                    AND account."PanLast4" ~ '^[0-9]{4}$';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CardWriteEvents_PanLast4",
                table: "CardWriteEvents",
                sql: "\"PanLast4\" IS NULL OR \"PanLast4\" ~ '^[0-9]{4}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CardReadEvents_PanLast4",
                table: "CardReadEvents",
                sql: "\"PanLast4\" IS NULL OR \"PanLast4\" ~ '^[0-9]{4}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema can be recreated, but discarded sensitive values are not recoverable.
            migrationBuilder.DropCheckConstraint(
                name: "CK_CardWriteEvents_PanLast4",
                table: "CardWriteEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CardReadEvents_PanLast4",
                table: "CardReadEvents");

            migrationBuilder.DropColumn(
                name: "PanLast4",
                table: "CardWriteEvents");

            migrationBuilder.DropColumn(
                name: "ReportedCardType",
                table: "CardWriteEvents");

            migrationBuilder.DropColumn(
                name: "PanLast4",
                table: "CardReadEvents");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "CardReadEvents");

            migrationBuilder.AddColumn<string>(
                name: "IntendedOperation",
                table: "CardWriteEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawPayloadJson",
                table: "CardWriteEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawPayloadJson",
                table: "CardReadEvents",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
