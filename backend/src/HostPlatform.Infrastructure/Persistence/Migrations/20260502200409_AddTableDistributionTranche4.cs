using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableDistributionTranche4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAtUtc",
                table: "TerminalTableAssignments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousTableSetId",
                table: "TerminalTableAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DependsOnTableDefinitionId",
                table: "TableVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadSha256Hex",
                table: "TableVersions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "TableVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TablePayloadId",
                table: "TableVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationDiagnosticsJson",
                table: "TableVersions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValidationPassed",
                table: "TableVersions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishGeneration",
                table: "TableSets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAtUtc",
                table: "TableSets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "TableSets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ErrorDetail",
                table: "DownloadBatchItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemStatus",
                table: "DownloadBatchItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "DownloadBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiagnosticsJson",
                table: "DownloadBatches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "DownloadBatches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartialDefinitionIdsJson",
                table: "DownloadBatches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "DownloadBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "DownloadBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CustomerTableOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTableOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerTableOverrides_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerTableOverrides_TableDefinitions_TableDefinitionId",
                        column: x => x.TableDefinitionId,
                        principalTable: "TableDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerTableOverrides_TableVersions_TableVersionId",
                        column: x => x.TableVersionId,
                        principalTable: "TableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteTableOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteTableOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteTableOverrides_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteTableOverrides_TableDefinitions_TableDefinitionId",
                        column: x => x.TableDefinitionId,
                        principalTable: "TableDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteTableOverrides_TableVersions_TableVersionId",
                        column: x => x.TableVersionId,
                        principalTable: "TableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TablePayloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RawContent = table.Column<byte[]>(type: "bytea", nullable: false),
                    Sha256Hex = table.Column<string>(type: "text", nullable: false),
                    LengthBytes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TablePayloads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TerminalTableOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalTableOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalTableOverrides_TableDefinitions_TableDefinitionId",
                        column: x => x.TableDefinitionId,
                        principalTable: "TableDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerminalTableOverrides_TableVersions_TableVersionId",
                        column: x => x.TableVersionId,
                        principalTable: "TableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerminalTableOverrides_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableAssignments_PreviousTableSetId",
                table: "TerminalTableAssignments",
                column: "PreviousTableSetId");

            migrationBuilder.CreateIndex(
                name: "IX_TableVersions_TablePayloadId",
                table: "TableVersions",
                column: "TablePayloadId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTableOverrides_CustomerId_TableDefinitionId",
                table: "CustomerTableOverrides",
                columns: new[] { "CustomerId", "TableDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTableOverrides_TableDefinitionId",
                table: "CustomerTableOverrides",
                column: "TableDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTableOverrides_TableVersionId",
                table: "CustomerTableOverrides",
                column: "TableVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteTableOverrides_SiteId_TableDefinitionId",
                table: "SiteTableOverrides",
                columns: new[] { "SiteId", "TableDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteTableOverrides_TableDefinitionId",
                table: "SiteTableOverrides",
                column: "TableDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteTableOverrides_TableVersionId",
                table: "SiteTableOverrides",
                column: "TableVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableOverrides_TableDefinitionId",
                table: "TerminalTableOverrides",
                column: "TableDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableOverrides_TableVersionId",
                table: "TerminalTableOverrides",
                column: "TableVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableOverrides_TerminalId_TableDefinitionId",
                table: "TerminalTableOverrides",
                columns: new[] { "TerminalId", "TableDefinitionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TableVersions_TablePayloads_TablePayloadId",
                table: "TableVersions",
                column: "TablePayloadId",
                principalTable: "TablePayloads",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TerminalTableAssignments_TableSets_PreviousTableSetId",
                table: "TerminalTableAssignments",
                column: "PreviousTableSetId",
                principalTable: "TableSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """UPDATE "TableVersions" SET "ValidationPassed" = false WHERE "Payload" IS NULL OR octet_length("Payload") = 0;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TableVersions_TablePayloads_TablePayloadId",
                table: "TableVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_TerminalTableAssignments_TableSets_PreviousTableSetId",
                table: "TerminalTableAssignments");

            migrationBuilder.DropTable(
                name: "CustomerTableOverrides");

            migrationBuilder.DropTable(
                name: "SiteTableOverrides");

            migrationBuilder.DropTable(
                name: "TablePayloads");

            migrationBuilder.DropTable(
                name: "TerminalTableOverrides");

            migrationBuilder.DropIndex(
                name: "IX_TerminalTableAssignments_PreviousTableSetId",
                table: "TerminalTableAssignments");

            migrationBuilder.DropIndex(
                name: "IX_TableVersions_TablePayloadId",
                table: "TableVersions");

            migrationBuilder.DropColumn(
                name: "AssignedAtUtc",
                table: "TerminalTableAssignments");

            migrationBuilder.DropColumn(
                name: "PreviousTableSetId",
                table: "TerminalTableAssignments");

            migrationBuilder.DropColumn(
                name: "DependsOnTableDefinitionId",
                table: "TableVersions");

            migrationBuilder.DropColumn(
                name: "PayloadSha256Hex",
                table: "TableVersions");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "TableVersions");

            migrationBuilder.DropColumn(
                name: "TablePayloadId",
                table: "TableVersions");

            migrationBuilder.DropColumn(
                name: "ValidationDiagnosticsJson",
                table: "TableVersions");

            migrationBuilder.DropColumn(
                name: "ValidationPassed",
                table: "TableVersions");

            migrationBuilder.DropColumn(
                name: "PublishGeneration",
                table: "TableSets");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "TableSets");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "TableSets");

            migrationBuilder.DropColumn(
                name: "ErrorDetail",
                table: "DownloadBatchItems");

            migrationBuilder.DropColumn(
                name: "ItemStatus",
                table: "DownloadBatchItems");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "DownloadBatches");

            migrationBuilder.DropColumn(
                name: "DiagnosticsJson",
                table: "DownloadBatches");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "DownloadBatches");

            migrationBuilder.DropColumn(
                name: "PartialDefinitionIdsJson",
                table: "DownloadBatches");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "DownloadBatches");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "DownloadBatches");
        }
    }
}
