using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoinLineBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Resource = table.Column<string>(type: "text", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    TerminalSessionId = table.Column<string>(type: "text", nullable: true),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "CardProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DefaultCardType = table.Column<int>(type: "integer", nullable: false),
                    AllowNegativeBalance = table.Column<bool>(type: "boolean", nullable: false),
                    IsTestFixtureCatalogEntry = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardProducts", x => x.Id);
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
                name: "CarrierLibraryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    CodeKey = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Description = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TableNumber = table.Column<int>(type: "integer", nullable: false),
                    RowPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierLibraryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CraftCommandTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    IsDestructive = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultSimulationOnly = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftCommandTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DlogMessageTypes",
                columns: table => new
                {
                    MtCode = table.Column<int>(type: "integer", nullable: false),
                    SymbolName = table.Column<string>(type: "text", nullable: false),
                    MessageAction = table.Column<string>(type: "text", nullable: false),
                    ImmediateClear = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlogMessageTypes", x => x.MtCode);
                });

            migrationBuilder.CreateTable(
                name: "DlogReplayRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilterJson = table.Column<string>(type: "text", nullable: false),
                    ResultSummaryJson = table.Column<string>(type: "text", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlogReplayRequests", x => x.Id);
                });

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
                name: "FirmwareVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    BuildId = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TableProfileId = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InmateFeatureConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Description = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SettingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InmateFeatureConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NccFrameCaptures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    ByteLength = table.Column<int>(type: "integer", nullable: false),
                    RawBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NccFrameCaptures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperatorAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SessionGeneration = table.Column<int>(type: "integer", nullable: false),
                    PermissionProfileJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuickAccessCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BillingComments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Street = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Province = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickAccessCustomers", x => x.Id);
                    table.CheckConstraint("CK_QuickAccessCustomers_Reference", "\"Reference\" ~ '^[0-9]{6}$' AND \"Reference\" <> '000000'");
                });

            migrationBuilder.CreateTable(
                name: "SmartcardTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    AtrProfile = table.Column<int>(type: "integer", nullable: false),
                    MapsToCardType = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartcardTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubsystemHeartbeats",
                columns: table => new
                {
                    Subsystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubsystemHeartbeats", x => x.Subsystem);
                });

            migrationBuilder.CreateTable(
                name: "TableDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TableNumber = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ContentChecksum = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableDefinitions", x => x.Id);
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
                name: "TransportEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    ConnectionString = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportEndpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DialedNumberClasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Pattern = table.Column<string>(type: "text", nullable: false),
                    MatchKind = table.Column<int>(type: "integer", nullable: false),
                    ClassName = table.Column<string>(type: "text", nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    IsFree = table.Column<bool>(type: "boolean", nullable: false),
                    IsEmergency = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DialedNumberClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DialedNumberClasses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sites_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalGroups_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TableSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetFirmwareVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishGeneration = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableSets_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TableSets_FirmwareVersions_TargetFirmwareVersionId",
                        column: x => x.TargetFirmwareVersionId,
                        principalTable: "FirmwareVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Terminals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportEndpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirmwareVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TerminalFamilyId = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    TerminalIdHex = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletionReviewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terminals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Terminals_FirmwareVersions_FirmwareVersionId",
                        column: x => x.FirmwareVersionId,
                        principalTable: "FirmwareVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Terminals_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Terminals_TerminalGroups_TerminalGroupId",
                        column: x => x.TerminalGroupId,
                        principalTable: "TerminalGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Terminals_TransportEndpoints_TransportEndpointId",
                        column: x => x.TransportEndpointId,
                        principalTable: "TransportEndpoints",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TableRecordUpdateRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    TargetsJson = table.Column<string>(type: "text", nullable: false),
                    PreviewToken = table.Column<string>(type: "text", nullable: false),
                    NextTarget = table.Column<int>(type: "integer", nullable: false),
                    ChangedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Stopped = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableRecordUpdateRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableRecordUpdateRuns_TableSets_SourceSetId",
                        column: x => x.SourceSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TableVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableRevision = table.Column<int>(type: "integer", nullable: false),
                    TablePayloadId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: true),
                    Checksum = table.Column<byte[]>(type: "bytea", nullable: true),
                    PayloadSha256Hex = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    DependsOnTableDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidationPassed = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationDiagnosticsJson = table.Column<string>(type: "text", nullable: true),
                    ScreeningDescriptionsJson = table.Column<string>(type: "text", nullable: true),
                    ScreeningLayoutProfile = table.Column<string>(type: "text", nullable: true),
                    ConfigurationId = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    CardMasterLinksJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableVersions_TableDefinitions_TableDefinitionId",
                        column: x => x.TableDefinitionId,
                        principalTable: "TableDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TableVersions_TablePayloads_TablePayloadId",
                        column: x => x.TablePayloadId,
                        principalTable: "TablePayloads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TableVersions_TableSets_TableSetId",
                        column: x => x.TableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapturedHardwareSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    SessionKind = table.Column<string>(type: "text", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceLabel = table.Column<string>(type: "text", nullable: false),
                    EnvelopeJson = table.Column<string>(type: "text", nullable: false),
                    EnvelopeChecksumSha256 = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapturedHardwareSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapturedHardwareSessions_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CardAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    PanLast4 = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric", nullable: false),
                    ResolvedCardType = table.Column<int>(type: "integer", nullable: false),
                    CredentialTokenRef = table.Column<string>(type: "text", nullable: false),
                    CredentialKind = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardAccounts_CardProducts_CardProductId",
                        column: x => x.CardProductId,
                        principalTable: "CardProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardAccounts_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CdrUploadRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SimulationMode = table.Column<bool>(type: "boolean", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    CraftSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CdrUploadRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CdrUploadRequests_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CraftSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicianId = table.Column<string>(type: "text", nullable: false),
                    OperatorId = table.Column<string>(type: "text", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FieldNotes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CraftSessions_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NccSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "text", nullable: false),
                    RawCaptureUri = table.Column<string>(type: "text", nullable: true),
                    LastFrameSample = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NccSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NccSessions_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RemoteTableReloadRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SimulationMode = table.Column<bool>(type: "boolean", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    CraftSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteTableReloadRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemoteTableReloadRequests_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalCallbackRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminalClockOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    DeliverAssignedTables = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProgrammedForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReturnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalCallbackRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalCallbackRequests_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalDiagnosticSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalDiagnosticSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalDiagnosticSnapshots_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalEvents_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalGroupMemberships",
                columns: table => new
                {
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalGroupId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalGroupMemberships", x => new { x.TerminalId, x.TerminalGroupId });
                    table.ForeignKey(
                        name: "FK_TerminalGroupMemberships_TerminalGroups_TerminalGroupId",
                        column: x => x.TerminalGroupId,
                        principalTable: "TerminalGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TerminalGroupMemberships_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalIdentityCutoverItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceWireId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TargetWireId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SourceTerminalRevision = table.Column<int>(type: "integer", nullable: false),
                    SourceFirmwareVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAssignmentRevision = table.Column<int>(type: "integer", nullable: false),
                    SourceTableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTableSetRevision = table.Column<int>(type: "integer", nullable: false),
                    SourceAccessRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccessRecordRevision = table.Column<int>(type: "integer", nullable: false),
                    SourcePayloadSha256Hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetPayloadSha256Hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StagedTableSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PossiblyAppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApplicationAcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransferEndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewIdentityObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewIdentityObservedSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivationError = table.Column<string>(type: "text", nullable: true),
                    ActivationSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransferError = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalIdentityCutoverItems", x => x.Id);
                    table.CheckConstraint("CK_TerminalIdentityCutoverItems_DecimalIds", "\"SourceWireId\" ~ '^[0-9]{10}$' AND \"TargetWireId\" ~ '^[0-9]{10}$' AND \"SourceWireId\" <> \"TargetWireId\"");
                    table.ForeignKey(
                        name: "FK_TerminalIdentityCutoverItems_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TerminalInmateConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureConfigurationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SetupConfigurationId = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalInmateConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalInmateConfigurations_InmateFeatureConfigurations_Fe~",
                        column: x => x.FeatureConfigurationId,
                        principalTable: "InmateFeatureConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TerminalInmateConfigurations_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalInventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Details_RegionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Details_TimeAreaCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Details_Route = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_Stop = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_SiteDescription = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details_AddressLine1 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details_AddressLine2 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details_AddressLine3 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details_Contact = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Details_Indoor = table.Column<bool>(type: "boolean", nullable: false),
                    Details_Basement = table.Column<bool>(type: "boolean", nullable: false),
                    Details_ServiceClass = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_PublicService = table.Column<bool>(type: "boolean", nullable: false),
                    Details_SalespersonCode = table.Column<string>(type: "text", nullable: true),
                    Details_RevenueAccountingCode = table.Column<string>(type: "text", nullable: true),
                    Details_BillingCustomerCode = table.Column<string>(type: "text", nullable: true),
                    Details_VoucherAccountCode = table.Column<string>(type: "text", nullable: true),
                    Details_IndustryCode = table.Column<string>(type: "text", nullable: true),
                    Details_LocationGroupCode = table.Column<string>(type: "text", nullable: true),
                    Details_EquipmentCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Details_LoopMileageCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Details_HousingCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_DirectoryCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_DirectoryCount = table.Column<int>(type: "integer", nullable: true),
                    Details_SignCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_SignCount = table.Column<int>(type: "integer", nullable: true),
                    Details_ExtensionCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_ExtensionCount = table.Column<int>(type: "integer", nullable: true),
                    Details_MiscellaneousCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Details_MiscellaneousCount = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalInventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalInventories_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalStatusRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalStatusRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalStatusRecords_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalTableAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousTableSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalTableAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalTableAssignments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TerminalTableAssignments_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TerminalTableAssignments_TableSets_PreviousTableSetId",
                        column: x => x.PreviousTableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TerminalTableAssignments_TableSets_TableSetId",
                        column: x => x.TableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerminalTableAssignments_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "BalanceAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Delta = table.Column<decimal>(type: "numeric", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    SimulationMode = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BalanceAdjustments_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    PanLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardReadEvents", x => x.Id);
                    table.CheckConstraint("CK_CardReadEvents_PanLast4", "\"PanLast4\" IS NULL OR \"PanLast4\" ~ '^[0-9]{4}$'");
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
                name: "CardWriteEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    PanLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    ReportedCardType = table.Column<int>(type: "integer", nullable: false),
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
                    table.CheckConstraint("CK_CardWriteEvents_PanLast4", "\"PanLast4\" IS NULL OR \"PanLast4\" ~ '^[0-9]{4}$'");
                    table.ForeignKey(
                        name: "FK_CardWriteEvents_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EPurseAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EPurseAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EPurseAccounts_CardAccounts_CardAccountId",
                        column: x => x.CardAccountId,
                        principalTable: "CardAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Reconciliation = table.Column<int>(type: "integer", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_CardAccounts_CardAccountId",
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

            migrationBuilder.CreateTable(
                name: "CraftAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CraftSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CraftAuditEvents_CraftSessions_CraftSessionId",
                        column: x => x.CraftSessionId,
                        principalTable: "CraftSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CraftCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CraftSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CraftCommandTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestRaw = table.Column<byte[]>(type: "bytea", nullable: false),
                    ResponseRaw = table.Column<byte[]>(type: "bytea", nullable: true),
                    CommandName = table.Column<string>(type: "text", nullable: false),
                    AuditReason = table.Column<string>(type: "text", nullable: false),
                    DestructiveConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    SimulationExecution = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CraftCommands_CraftCommandTypes_CraftCommandTypeId",
                        column: x => x.CraftCommandTypeId,
                        principalTable: "CraftCommandTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CraftCommands_CraftSessions_CraftSessionId",
                        column: x => x.CraftSessionId,
                        principalTable: "CraftSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CraftDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CraftSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftDiagnostics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CraftDiagnostics_CraftSessions_CraftSessionId",
                        column: x => x.CraftSessionId,
                        principalTable: "CraftSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CraftDiagnostics_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DlogTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    MessageType = table.Column<int>(type: "integer", nullable: false),
                    MessageTypeName = table.Column<string>(type: "text", nullable: false),
                    CorrelationKey = table.Column<int>(type: "integer", nullable: true),
                    RawPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    DecodedJson = table.Column<string>(type: "text", nullable: false),
                    IsUnknownMessageType = table.Column<bool>(type: "boolean", nullable: false),
                    ImmediateClear = table.Column<bool>(type: "boolean", nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    NccSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionCorrelationId = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlogTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DlogTransactions_NccSessions_NccSessionId",
                        column: x => x.NccSessionId,
                        principalTable: "NccSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DlogTransactions_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DownloadBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientIdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    NccSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    PartialDefinitionIdsJson = table.Column<string>(type: "text", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    DiagnosticsJson = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadBatches_NccSessions_NccSessionId",
                        column: x => x.NccSessionId,
                        principalTable: "NccSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DownloadBatches_TableSets_TableSetId",
                        column: x => x.TableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadBatches_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NccSessionFrames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NccSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    SentByHost = table.Column<bool>(type: "boolean", nullable: false),
                    LinkControl = table.Column<byte>(type: "smallint", nullable: true),
                    WireLength = table.Column<int>(type: "integer", nullable: true),
                    TerminalIdHex = table.Column<string>(type: "text", nullable: false),
                    TerminalMatched = table.Column<bool>(type: "boolean", nullable: false),
                    RawBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NccSessionFrames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NccSessionFrames_NccSessions_NccSessionId",
                        column: x => x.NccSessionId,
                        principalTable: "NccSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DlogCorrelationLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkRule = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlogCorrelationLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DlogCorrelationLinks_DlogTransactions_RequestTransactionId",
                        column: x => x.RequestTransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DlogCorrelationLinks_DlogTransactions_ResponseTransactionId",
                        column: x => x.ResponseTransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DlogParseDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DlogTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlogParseDiagnostics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DlogParseDiagnostics_DlogTransactions_DlogTransactionId",
                        column: x => x.DlogTransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "UploadBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    DecodedMetadataJson = table.Column<string>(type: "text", nullable: false),
                    RelatedDlogTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadBatches_DlogTransactions_RelatedDlogTransactionId",
                        column: x => x.RelatedDlogTransactionId,
                        principalTable: "DlogTransactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadBatches_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DownloadBatchItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DownloadBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepIndex = table.Column<int>(type: "integer", nullable: false),
                    LastAckStatus = table.Column<string>(type: "text", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    ItemStatus = table.Column<int>(type: "integer", nullable: false),
                    HostDownloadPhase = table.Column<int>(type: "integer", nullable: false),
                    ErrorDetail = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadBatchItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadBatchItems_DownloadBatches_DownloadBatchId",
                        column: x => x.DownloadBatchId,
                        principalTable: "DownloadBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadBatchItems_TableVersions_TableVersionId",
                        column: x => x.TableVersionId,
                        principalTable: "TableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledTableDownloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TableSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTableSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirmwareSlotEndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordNumbers = table.Column<int[]>(type: "integer[]", nullable: true),
                    NccSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTableDownloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_DownloadBatches_LastBatchId",
                        column: x => x.LastBatchId,
                        principalTable: "DownloadBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_NccSessions_NccSessionId",
                        column: x => x.NccSessionId,
                        principalTable: "NccSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_TableSets_SourceTableSetId",
                        column: x => x.SourceTableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_TableSets_TableSetId",
                        column: x => x.TableSetId,
                        principalTable: "TableSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledTableDownloads_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    DecodedMetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadRecords_UploadBatches_UploadBatchId",
                        column: x => x.UploadBatchId,
                        principalTable: "UploadBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                });

            migrationBuilder.CreateTable(
                name: "CallRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    DialedDigits = table.Column<string>(type: "text", nullable: false),
                    RequestRaw = table.Column<byte[]>(type: "bytea", nullable: false),
                    ResponseRaw = table.Column<byte[]>(type: "bytea", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Disposition = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reconciliation = table.Column<int>(type: "integer", nullable: false),
                    AppliedRatePlanVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallRecords_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
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
                });

            migrationBuilder.CreateTable(
                name: "FirmwareArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwarePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Sha256Hex = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageRef = table.Column<string>(type: "text", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareArtifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FirmwarePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    VersionLabel = table.Column<string>(type: "text", nullable: false),
                    ArtifactChecksum = table.Column<byte[]>(type: "bytea", nullable: false),
                    ArtifactSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    PrimaryArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwarePackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwarePackages_FirmwareArtifacts_PrimaryArtifactId",
                        column: x => x.PrimaryArtifactId,
                        principalTable: "FirmwareArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareBlockManifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwarePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwareArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    LayoutJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareBlockManifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareBlockManifests_FirmwareArtifacts_FirmwareArtifactId",
                        column: x => x.FirmwareArtifactId,
                        principalTable: "FirmwareArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FirmwareBlockManifests_FirmwarePackages_FirmwarePackageId",
                        column: x => x.FirmwarePackageId,
                        principalTable: "FirmwarePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareCompatibilityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwarePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredTerminalFirmwareVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiredTargetSkuContains = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareCompatibilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareCompatibilityRules_FirmwarePackages_FirmwarePackage~",
                        column: x => x.FirmwarePackageId,
                        principalTable: "FirmwarePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FirmwareCompatibilityRules_FirmwareVersions_RequiredTermina~",
                        column: x => x.RequiredTerminalFirmwareVersionId,
                        principalTable: "FirmwareVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwarePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareTargets_FirmwarePackages_FirmwarePackageId",
                        column: x => x.FirmwarePackageId,
                        principalTable: "FirmwarePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareUpdateJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwarePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwareArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SimulationMode = table.Column<bool>(type: "boolean", nullable: false),
                    SafetyStateJson = table.Column<string>(type: "text", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByOperatorId = table.Column<string>(type: "text", nullable: false),
                    CancelReason = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareUpdateJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareUpdateJobs_FirmwareArtifacts_FirmwareArtifactId",
                        column: x => x.FirmwareArtifactId,
                        principalTable: "FirmwareArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FirmwareUpdateJobs_FirmwarePackages_FirmwarePackageId",
                        column: x => x.FirmwarePackageId,
                        principalTable: "FirmwarePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FirmwareUpdateJobs_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareRollBackPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwareUpdateJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    BackupNotes = table.Column<string>(type: "text", nullable: false),
                    RecoveryStepsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareRollBackPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareRollBackPlans_FirmwareUpdateJobs_FirmwareUpdateJobId",
                        column: x => x.FirmwareUpdateJobId,
                        principalTable: "FirmwareUpdateJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareUpdateSafetyChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwareUpdateJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareUpdateSafetyChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareUpdateSafetyChecks_FirmwareUpdateJobs_FirmwareUpdat~",
                        column: x => x.FirmwareUpdateJobId,
                        principalTable: "FirmwareUpdateJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmwareUpdateSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmwareUpdateJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepIndex = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StepStatus = table.Column<int>(type: "integer", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareUpdateSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmwareUpdateSteps_FirmwareUpdateJobs_FirmwareUpdateJobId",
                        column: x => x.FirmwareUpdateJobId,
                        principalTable: "FirmwareUpdateJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RatePlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatePlans_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                });

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

            migrationBuilder.CreateTable(
                name: "RateRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    MatchKind = table.Column<int>(type: "integer", nullable: false),
                    Pattern = table.Column<string>(type: "text", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    RatePerMinuteUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    Expression = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RateRules_RatePlanVersions_RatePlanVersionId",
                        column: x => x.RatePlanVersionId,
                        principalTable: "RatePlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RatingResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    Blocked = table.Column<bool>(type: "boolean", nullable: false),
                    FreeCall = table.Column<bool>(type: "boolean", nullable: false),
                    Emergency = table.Column<bool>(type: "boolean", nullable: false),
                    DecisionKind = table.Column<int>(type: "integer", nullable: false),
                    RatePlanVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeterminismInputJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatingResults_CallRecords_CallRecordId",
                        column: x => x.CallRecordId,
                        principalTable: "CallRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RatingResults_RatePlanVersions_RatePlanVersionId",
                        column: x => x.RatePlanVersionId,
                        principalTable: "RatePlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "IX_AuditEvents_CreatedAtUtc",
                table: "AuditEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TerminalId_CreatedAtUtc",
                table: "AuditEvents",
                columns: new[] { "TerminalId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceAdjustments_CardAccountId",
                table: "BalanceAdjustments",
                column: "CardAccountId");

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
                name: "IX_CallRecords_AppliedRatePlanVersionId",
                table: "CallRecords",
                column: "AppliedRatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_TerminalId",
                table: "CallRecords",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_CapturedHardwareSessions_CreatedAtUtc",
                table: "CapturedHardwareSessions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CapturedHardwareSessions_EnvelopeChecksumSha256",
                table: "CapturedHardwareSessions",
                column: "EnvelopeChecksumSha256");

            migrationBuilder.CreateIndex(
                name: "IX_CapturedHardwareSessions_SessionKind",
                table: "CapturedHardwareSessions",
                column: "SessionKind");

            migrationBuilder.CreateIndex(
                name: "IX_CapturedHardwareSessions_TerminalId",
                table: "CapturedHardwareSessions",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_CardAccounts_CardProductId",
                table: "CardAccounts",
                column: "CardProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CardAccounts_TerminalId",
                table: "CardAccounts",
                column: "TerminalId");

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
                name: "IX_CardMasterDefinitions_TableNumber_CodeKey",
                table: "CardMasterDefinitions",
                columns: new[] { "TableNumber", "CodeKey" },
                unique: true);

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
                name: "IX_CarrierLibraryEntries_TableNumber_CodeKey",
                table: "CarrierLibraryEntries",
                columns: new[] { "TableNumber", "CodeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CdrUploadRequests_TerminalId",
                table: "CdrUploadRequests",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftAuditEvents_CraftSessionId",
                table: "CraftAuditEvents",
                column: "CraftSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftCommands_CraftCommandTypeId",
                table: "CraftCommands",
                column: "CraftCommandTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftCommands_CraftSessionId",
                table: "CraftCommands",
                column: "CraftSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftCommandTypes_Code",
                table: "CraftCommandTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CraftDiagnostics_CraftSessionId",
                table: "CraftDiagnostics",
                column: "CraftSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftDiagnostics_TerminalId",
                table: "CraftDiagnostics",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftSessions_TerminalId",
                table: "CraftSessions",
                column: "TerminalId");

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
                name: "IX_DestinationPrefixes_RatePlanVersionId",
                table: "DestinationPrefixes",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationPrefixes_TariffId",
                table: "DestinationPrefixes",
                column: "TariffId");

            migrationBuilder.CreateIndex(
                name: "IX_DialedNumberClasses_CustomerId",
                table: "DialedNumberClasses",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DlogCorrelationLinks_RequestTransactionId",
                table: "DlogCorrelationLinks",
                column: "RequestTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_DlogCorrelationLinks_ResponseTransactionId",
                table: "DlogCorrelationLinks",
                column: "ResponseTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_DlogParseDiagnostics_DlogTransactionId",
                table: "DlogParseDiagnostics",
                column: "DlogTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_DlogTransactions_IdempotencyKey",
                table: "DlogTransactions",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL AND \"IdempotencyKey\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DlogTransactions_NccSessionId",
                table: "DlogTransactions",
                column: "NccSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DlogTransactions_TerminalId",
                table: "DlogTransactions",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatches_ClientIdempotencyKey",
                table: "DownloadBatches",
                column: "ClientIdempotencyKey",
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL AND \"ClientIdempotencyKey\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatches_NccSessionId",
                table: "DownloadBatches",
                column: "NccSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatches_TableSetId",
                table: "DownloadBatches",
                column: "TableSetId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatches_TerminalId",
                table: "DownloadBatches",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatches_TerminalId_ActiveTransfer",
                table: "DownloadBatches",
                column: "TerminalId",
                unique: true,
                filter: "\"NccSessionId\" IS NOT NULL AND \"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatchItems_DownloadBatchId",
                table: "DownloadBatchItems",
                column: "DownloadBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadBatchItems_TableVersionId",
                table: "DownloadBatchItems",
                column: "TableVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_EPurseAccounts_CardAccountId",
                table: "EPurseAccounts",
                column: "CardAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EPurseProfiles_CardAccountId",
                table: "EPurseProfiles",
                column: "CardAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldEventCodeSettings_Kind_Code",
                table: "FieldEventCodeSettings",
                columns: new[] { "Kind", "Code" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareArtifacts_FirmwarePackageId",
                table: "FirmwareArtifacts",
                column: "FirmwarePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareBlockManifests_FirmwareArtifactId",
                table: "FirmwareBlockManifests",
                column: "FirmwareArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareBlockManifests_FirmwarePackageId",
                table: "FirmwareBlockManifests",
                column: "FirmwarePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareCompatibilityRules_FirmwarePackageId",
                table: "FirmwareCompatibilityRules",
                column: "FirmwarePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareCompatibilityRules_RequiredTerminalFirmwareVersionId",
                table: "FirmwareCompatibilityRules",
                column: "RequiredTerminalFirmwareVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwarePackages_PrimaryArtifactId",
                table: "FirmwarePackages",
                column: "PrimaryArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareRollBackPlans_FirmwareUpdateJobId",
                table: "FirmwareRollBackPlans",
                column: "FirmwareUpdateJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareTargets_FirmwarePackageId",
                table: "FirmwareTargets",
                column: "FirmwarePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateJobs_FirmwareArtifactId",
                table: "FirmwareUpdateJobs",
                column: "FirmwareArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateJobs_FirmwarePackageId",
                table: "FirmwareUpdateJobs",
                column: "FirmwarePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateJobs_TerminalId_Status",
                table: "FirmwareUpdateJobs",
                columns: new[] { "TerminalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateSafetyChecks_FirmwareUpdateJobId",
                table: "FirmwareUpdateSafetyChecks",
                column: "FirmwareUpdateJobId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareUpdateSteps_FirmwareUpdateJobId",
                table: "FirmwareUpdateSteps",
                column: "FirmwareUpdateJobId");

            migrationBuilder.CreateIndex(
                name: "IX_InmateFeatureConfigurations_NameKey",
                table: "InmateFeatureConfigurations",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NccSessionFrames_NccSessionId_Sequence",
                table: "NccSessionFrames",
                columns: new[] { "NccSessionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_NccSessions_TerminalId",
                table: "NccSessions",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorAccounts_Username",
                table: "OperatorAccounts",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_CardAccountId",
                table: "PaymentTransactions",
                column: "CardAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_QuickAccessCustomers_Reference",
                table: "QuickAccessCustomers",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RatePlans_CustomerId",
                table: "RatePlans",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RatePlans_PublishedVersionId",
                table: "RatePlans",
                column: "PublishedVersionId");

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
                name: "IX_RatingResults_CallRecordId",
                table: "RatingResults",
                column: "CallRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_RatingResults_RatePlanVersionId",
                table: "RatingResults",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteTableReloadRequests_TerminalId",
                table: "RemoteTableReloadRequests",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_AvailableAtUtc",
                table: "ScheduledTableDownloads",
                column: "AvailableAtUtc",
                filter: "\"LastBatchId\" IS NULL AND \"NccSessionId\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_LastBatchId",
                table: "ScheduledTableDownloads",
                column: "LastBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_NccSessionId",
                table: "ScheduledTableDownloads",
                column: "NccSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_SourceTableSetId",
                table: "ScheduledTableDownloads",
                column: "SourceTableSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_TableSetId_AvailableAtUtc",
                table: "ScheduledTableDownloads",
                columns: new[] { "TableSetId", "AvailableAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTableDownloads_TerminalId",
                table: "ScheduledTableDownloads",
                column: "TerminalId",
                unique: true,
                filter: "\"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_CustomerId",
                table: "Sites",
                column: "CustomerId");

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
                name: "IX_SmartcardProfiles_CardAccountId",
                table: "SmartcardProfiles",
                column: "CardAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartcardProfiles_SmartcardTypeId",
                table: "SmartcardProfiles",
                column: "SmartcardTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TableRecordUpdateRuns_SourceSetId_EndedAtUtc",
                table: "TableRecordUpdateRuns",
                columns: new[] { "SourceSetId", "EndedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TableSets_CustomerId",
                table: "TableSets",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TableSets_TargetFirmwareVersionId",
                table: "TableSets",
                column: "TargetFirmwareVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TableVersions_ConfigurationId_TableDefinitionId",
                table: "TableVersions",
                columns: new[] { "ConfigurationId", "TableDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_TableVersions_TableDefinitionId",
                table: "TableVersions",
                column: "TableDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_TableVersions_TablePayloadId",
                table: "TableVersions",
                column: "TablePayloadId");

            migrationBuilder.CreateIndex(
                name: "IX_TableVersions_TableSetId",
                table: "TableVersions",
                column: "TableSetId");

            migrationBuilder.CreateIndex(
                name: "IX_Tariffs_RatePlanVersionId",
                table: "Tariffs",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalCallbackRequests_TerminalId",
                table: "TerminalCallbackRequests",
                column: "TerminalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TerminalDiagnosticSnapshots_TerminalId",
                table: "TerminalDiagnosticSnapshots",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalEvents_TerminalId",
                table: "TerminalEvents",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalGroupMemberships_TerminalGroupId",
                table: "TerminalGroupMemberships",
                column: "TerminalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalGroups_CustomerId",
                table: "TerminalGroups",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalIdentityCutoverItems_DueAtUtc",
                table: "TerminalIdentityCutoverItems",
                column: "DueAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalIdentityCutoverItems_JobId",
                table: "TerminalIdentityCutoverItems",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalIdentityCutoverItems_TargetWireId",
                table: "TerminalIdentityCutoverItems",
                column: "TargetWireId",
                unique: true,
                filter: "\"FinishedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalIdentityCutoverItems_TerminalId",
                table: "TerminalIdentityCutoverItems",
                column: "TerminalId",
                unique: true,
                filter: "\"FinishedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalInmateConfigurations_FeatureConfigurationId",
                table: "TerminalInmateConfigurations",
                column: "FeatureConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalInmateConfigurations_TerminalId",
                table: "TerminalInmateConfigurations",
                column: "TerminalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TerminalInventories_TerminalId",
                table: "TerminalInventories",
                column: "TerminalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_FirmwareVersionId",
                table: "Terminals",
                column: "FirmwareVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_SiteId",
                table: "Terminals",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_TerminalGroupId",
                table: "Terminals",
                column: "TerminalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_TransportEndpointId",
                table: "Terminals",
                column: "TransportEndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalStatusRecords_TerminalId",
                table: "TerminalStatusRecords",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableAssignments_CustomerId",
                table: "TerminalTableAssignments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableAssignments_PreviousTableSetId",
                table: "TerminalTableAssignments",
                column: "PreviousTableSetId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableAssignments_SiteId",
                table: "TerminalTableAssignments",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableAssignments_TableSetId",
                table: "TerminalTableAssignments",
                column: "TableSetId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalTableAssignments_TerminalId",
                table: "TerminalTableAssignments",
                column: "TerminalId",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_TimeBands_RatePlanVersionId",
                table: "TimeBands",
                column: "RatePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeBands_TariffId",
                table: "TimeBands",
                column: "TariffId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatches_IdempotencyKey",
                table: "UploadBatches",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatches_RelatedDlogTransactionId",
                table: "UploadBatches",
                column: "RelatedDlogTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatches_TerminalId",
                table: "UploadBatches",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadRecords_UploadBatchId",
                table: "UploadRecords",
                column: "UploadBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_CallAuthorizationRequests_RatePlans_RatePlanId",
                table: "CallAuthorizationRequests",
                column: "RatePlanId",
                principalTable: "RatePlans",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CallChargeSegments_RatingResults_RatingResultId",
                table: "CallChargeSegments",
                column: "RatingResultId",
                principalTable: "RatingResults",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CallRecords_RatePlanVersions_AppliedRatePlanVersionId",
                table: "CallRecords",
                column: "AppliedRatePlanVersionId",
                principalTable: "RatePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DestinationPrefixes_RatePlanVersions_RatePlanVersionId",
                table: "DestinationPrefixes",
                column: "RatePlanVersionId",
                principalTable: "RatePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DestinationPrefixes_Tariffs_TariffId",
                table: "DestinationPrefixes",
                column: "TariffId",
                principalTable: "Tariffs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FirmwareArtifacts_FirmwarePackages_FirmwarePackageId",
                table: "FirmwareArtifacts",
                column: "FirmwarePackageId",
                principalTable: "FirmwarePackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RatePlans_RatePlanVersions_PublishedVersionId",
                table: "RatePlans",
                column: "PublishedVersionId",
                principalTable: "RatePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RatePlanVersions_RatePlans_RatePlanId",
                table: "RatePlanVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_FirmwareArtifacts_FirmwarePackages_FirmwarePackageId",
                table: "FirmwareArtifacts");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "BalanceAdjustments");

            migrationBuilder.DropTable(
                name: "CallAuthorizationRequests");

            migrationBuilder.DropTable(
                name: "CallChargeSegments");

            migrationBuilder.DropTable(
                name: "CapturedHardwareSessions");

            migrationBuilder.DropTable(
                name: "CardBalances");

            migrationBuilder.DropTable(
                name: "CardCredentials");

            migrationBuilder.DropTable(
                name: "CardMasterDefinitions");

            migrationBuilder.DropTable(
                name: "CardReadEvents");

            migrationBuilder.DropTable(
                name: "CardReconciliationBatches");

            migrationBuilder.DropTable(
                name: "CardWriteEvents");

            migrationBuilder.DropTable(
                name: "CarrierLibraryEntries");

            migrationBuilder.DropTable(
                name: "CdrUploadRequests");

            migrationBuilder.DropTable(
                name: "CraftAuditEvents");

            migrationBuilder.DropTable(
                name: "CraftCommands");

            migrationBuilder.DropTable(
                name: "CraftDiagnostics");

            migrationBuilder.DropTable(
                name: "CustomerTableOverrides");

            migrationBuilder.DropTable(
                name: "DestinationPrefixes");

            migrationBuilder.DropTable(
                name: "DialedNumberClasses");

            migrationBuilder.DropTable(
                name: "DlogCorrelationLinks");

            migrationBuilder.DropTable(
                name: "DlogMessageTypes");

            migrationBuilder.DropTable(
                name: "DlogParseDiagnostics");

            migrationBuilder.DropTable(
                name: "DlogReplayRequests");

            migrationBuilder.DropTable(
                name: "DownloadBatchItems");

            migrationBuilder.DropTable(
                name: "EPurseAccounts");

            migrationBuilder.DropTable(
                name: "EPurseProfiles");

            migrationBuilder.DropTable(
                name: "FieldEventCodeSettings");

            migrationBuilder.DropTable(
                name: "FieldEventDestinationRules");

            migrationBuilder.DropTable(
                name: "FieldEventRouteDecisions");

            migrationBuilder.DropTable(
                name: "FirmwareBlockManifests");

            migrationBuilder.DropTable(
                name: "FirmwareCompatibilityRules");

            migrationBuilder.DropTable(
                name: "FirmwareRollBackPlans");

            migrationBuilder.DropTable(
                name: "FirmwareTargets");

            migrationBuilder.DropTable(
                name: "FirmwareUpdateSafetyChecks");

            migrationBuilder.DropTable(
                name: "FirmwareUpdateSteps");

            migrationBuilder.DropTable(
                name: "NccFrameCaptures");

            migrationBuilder.DropTable(
                name: "NccSessionFrames");

            migrationBuilder.DropTable(
                name: "OperatorAccounts");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "QuickAccessCustomers");

            migrationBuilder.DropTable(
                name: "RateRules");

            migrationBuilder.DropTable(
                name: "RatingDiagnostics");

            migrationBuilder.DropTable(
                name: "RemoteTableReloadRequests");

            migrationBuilder.DropTable(
                name: "ScheduledTableDownloads");

            migrationBuilder.DropTable(
                name: "SiteTableOverrides");

            migrationBuilder.DropTable(
                name: "SmartcardProfiles");

            migrationBuilder.DropTable(
                name: "SubsystemHeartbeats");

            migrationBuilder.DropTable(
                name: "TableRecordUpdateRuns");

            migrationBuilder.DropTable(
                name: "TerminalCallbackRequests");

            migrationBuilder.DropTable(
                name: "TerminalDiagnosticSnapshots");

            migrationBuilder.DropTable(
                name: "TerminalEvents");

            migrationBuilder.DropTable(
                name: "TerminalGroupMemberships");

            migrationBuilder.DropTable(
                name: "TerminalIdentityCutoverItems");

            migrationBuilder.DropTable(
                name: "TerminalInmateConfigurations");

            migrationBuilder.DropTable(
                name: "TerminalInventories");

            migrationBuilder.DropTable(
                name: "TerminalStatusRecords");

            migrationBuilder.DropTable(
                name: "TerminalTableAssignments");

            migrationBuilder.DropTable(
                name: "TerminalTableOverrides");

            migrationBuilder.DropTable(
                name: "TimeBands");

            migrationBuilder.DropTable(
                name: "UploadRecords");

            migrationBuilder.DropTable(
                name: "CraftCommandTypes");

            migrationBuilder.DropTable(
                name: "CraftSessions");

            migrationBuilder.DropTable(
                name: "FirmwareUpdateJobs");

            migrationBuilder.DropTable(
                name: "RatingResults");

            migrationBuilder.DropTable(
                name: "DownloadBatches");

            migrationBuilder.DropTable(
                name: "CardAccounts");

            migrationBuilder.DropTable(
                name: "SmartcardTypes");

            migrationBuilder.DropTable(
                name: "InmateFeatureConfigurations");

            migrationBuilder.DropTable(
                name: "TableVersions");

            migrationBuilder.DropTable(
                name: "Tariffs");

            migrationBuilder.DropTable(
                name: "UploadBatches");

            migrationBuilder.DropTable(
                name: "CallRecords");

            migrationBuilder.DropTable(
                name: "CardProducts");

            migrationBuilder.DropTable(
                name: "TableDefinitions");

            migrationBuilder.DropTable(
                name: "TablePayloads");

            migrationBuilder.DropTable(
                name: "TableSets");

            migrationBuilder.DropTable(
                name: "DlogTransactions");

            migrationBuilder.DropTable(
                name: "NccSessions");

            migrationBuilder.DropTable(
                name: "Terminals");

            migrationBuilder.DropTable(
                name: "FirmwareVersions");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropTable(
                name: "TerminalGroups");

            migrationBuilder.DropTable(
                name: "TransportEndpoints");

            migrationBuilder.DropTable(
                name: "RatePlans");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "RatePlanVersions");

            migrationBuilder.DropTable(
                name: "FirmwarePackages");

            migrationBuilder.DropTable(
                name: "FirmwareArtifacts");
        }
    }
}
