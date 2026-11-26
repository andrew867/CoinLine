CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "AuditEvents" (
        "Id" uuid NOT NULL,
        "Category" text NOT NULL,
        "Action" text NOT NULL,
        "Actor" text NOT NULL,
        "Resource" text NOT NULL,
        "DetailJson" text NOT NULL,
        "CorrelationId" text,
        "TerminalSessionId" text,
        "TerminalId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_AuditEvents" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "CardProducts" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "Code" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CardProducts" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "Customers" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "Code" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_Customers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "DialedNumberClasses" (
        "Id" uuid NOT NULL,
        "Pattern" text NOT NULL,
        "ClassName" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_DialedNumberClasses" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "DlogReplayRequests" (
        "Id" uuid NOT NULL,
        "FilterJson" text NOT NULL,
        "RequestedAtUtc" timestamp with time zone NOT NULL,
        "RequestedBy" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_DlogReplayRequests" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "FirmwarePackages" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "VersionLabel" text NOT NULL,
        "ArtifactChecksum" bytea NOT NULL,
        "ArtifactSizeBytes" bigint NOT NULL,
        "MetadataJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwarePackages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "FirmwareVersions" (
        "Id" uuid NOT NULL,
        "Label" text NOT NULL,
        "BuildId" text,
        "Notes" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareVersions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "SmartcardTypes" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "AtrProfile" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_SmartcardTypes" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "TableDefinitions" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "TableNumber" integer NOT NULL,
        "Description" text,
        "ContentChecksum" bytea,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TableDefinitions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "TransportEndpoints" (
        "Id" uuid NOT NULL,
        "Kind" integer NOT NULL,
        "DisplayName" text NOT NULL,
        "ConnectionString" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TransportEndpoints" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "RatePlans" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "CustomerId" uuid,
        "Mode" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_RatePlans" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RatePlans_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "Sites" (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "Name" text NOT NULL,
        "Code" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_Sites" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Sites_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "TableSets" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "CustomerId" uuid,
        "IsDefault" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TableSets" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TableSets_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "TerminalGroups" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "CustomerId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalGroups" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalGroups_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "FirmwareTargets" (
        "Id" uuid NOT NULL,
        "FirmwarePackageId" uuid NOT NULL,
        "Sku" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareTargets" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FirmwareTargets_FirmwarePackages_FirmwarePackageId" FOREIGN KEY ("FirmwarePackageId") REFERENCES "FirmwarePackages" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "RateRules" (
        "Id" uuid NOT NULL,
        "RatePlanId" uuid NOT NULL,
        "Expression" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_RateRules" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RateRules_RatePlans_RatePlanId" FOREIGN KEY ("RatePlanId") REFERENCES "RatePlans" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "TableVersions" (
        "Id" uuid NOT NULL,
        "TableSetId" uuid NOT NULL,
        "TableDefinitionId" uuid NOT NULL,
        "TableRevision" integer NOT NULL,
        "Payload" bytea,
        "Checksum" bytea,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TableVersions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TableVersions_TableDefinitions_TableDefinitionId" FOREIGN KEY ("TableDefinitionId") REFERENCES "TableDefinitions" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_TableVersions_TableSets_TableSetId" FOREIGN KEY ("TableSetId") REFERENCES "TableSets" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "Terminals" (
        "Id" uuid NOT NULL,
        "SiteId" uuid NOT NULL,
        "TerminalGroupId" uuid,
        "TransportEndpointId" uuid,
        "FirmwareVersionId" uuid,
        "TerminalIdHex" text NOT NULL,
        "DisplayName" text NOT NULL,
        "Status" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_Terminals" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Terminals_FirmwareVersions_FirmwareVersionId" FOREIGN KEY ("FirmwareVersionId") REFERENCES "FirmwareVersions" ("Id"),
        CONSTRAINT "FK_Terminals_Sites_SiteId" FOREIGN KEY ("SiteId") REFERENCES "Sites" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_Terminals_TerminalGroups_TerminalGroupId" FOREIGN KEY ("TerminalGroupId") REFERENCES "TerminalGroups" ("Id"),
        CONSTRAINT "FK_Terminals_TransportEndpoints_TransportEndpointId" FOREIGN KEY ("TransportEndpointId") REFERENCES "TransportEndpoints" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "CallRecords" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid,
        "DialedDigits" text NOT NULL,
        "Mode" integer NOT NULL,
        "Disposition" integer NOT NULL,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "EndedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CallRecords" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CallRecords_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "CardAccounts" (
        "Id" uuid NOT NULL,
        "CardProductId" uuid NOT NULL,
        "TerminalId" uuid,
        "PanLast4" text NOT NULL,
        "Balance" numeric NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CardAccounts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CardAccounts_CardProducts_CardProductId" FOREIGN KEY ("CardProductId") REFERENCES "CardProducts" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_CardAccounts_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "CraftSessions" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "TechnicianId" text NOT NULL,
        "OperatorId" text NOT NULL,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "EndedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CraftSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CraftSessions_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "DlogTransactions" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid,
        "MessageType" integer NOT NULL,
        "MessageTypeName" text NOT NULL,
        "CorrelationKey" integer,
        "RawPayload" bytea NOT NULL,
        "DecodedJson" text NOT NULL,
        "IsUnknownMessageType" boolean NOT NULL,
        "ImmediateClear" boolean NOT NULL,
        "CapturedAtUtc" timestamp with time zone NOT NULL,
        "SessionCorrelationId" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_DlogTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DlogTransactions_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "DownloadBatches" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "TableSetId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_DownloadBatches" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DownloadBatches_TableSets_TableSetId" FOREIGN KEY ("TableSetId") REFERENCES "TableSets" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_DownloadBatches_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "FirmwareUpdateJobs" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "FirmwarePackageId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "SimulationMode" boolean NOT NULL,
        "SafetyStateJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareUpdateJobs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FirmwareUpdateJobs_FirmwarePackages_FirmwarePackageId" FOREIGN KEY ("FirmwarePackageId") REFERENCES "FirmwarePackages" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_FirmwareUpdateJobs_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "NccSessions" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "EndedAtUtc" timestamp with time zone,
        "CorrelationId" text NOT NULL,
        "RawCaptureUri" text,
        "LastFrameSample" bytea,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_NccSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_NccSessions_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "TerminalEvents" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "EventType" text NOT NULL,
        "PayloadJson" text NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalEvents_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "TerminalStatusRecords" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "Detail" text NOT NULL,
        "RecordedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalStatusRecords" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalStatusRecords_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "TerminalTableAssignments" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "TableSetId" uuid NOT NULL,
        "SiteId" uuid,
        "CustomerId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalTableAssignments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalTableAssignments_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
        CONSTRAINT "FK_TerminalTableAssignments_Sites_SiteId" FOREIGN KEY ("SiteId") REFERENCES "Sites" ("Id"),
        CONSTRAINT "FK_TerminalTableAssignments_TableSets_TableSetId" FOREIGN KEY ("TableSetId") REFERENCES "TableSets" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_TerminalTableAssignments_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "RatingResults" (
        "Id" uuid NOT NULL,
        "CallRecordId" uuid NOT NULL,
        "Amount" numeric NOT NULL,
        "Currency" text NOT NULL,
        "DetailJson" text NOT NULL,
        "Blocked" boolean NOT NULL,
        "FreeCall" boolean NOT NULL,
        "Emergency" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_RatingResults" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RatingResults_CallRecords_CallRecordId" FOREIGN KEY ("CallRecordId") REFERENCES "CallRecords" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "BalanceAdjustments" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid NOT NULL,
        "Delta" numeric NOT NULL,
        "Reason" text NOT NULL,
        "SimulationMode" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_BalanceAdjustments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BalanceAdjustments_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "EPurseAccounts" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid NOT NULL,
        "Balance" numeric NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_EPurseAccounts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_EPurseAccounts_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "PaymentTransactions" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid NOT NULL,
        "Amount" numeric NOT NULL,
        "Reconciliation" integer NOT NULL,
        "DetailJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_PaymentTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PaymentTransactions_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "CraftAuditEvents" (
        "Id" uuid NOT NULL,
        "CraftSessionId" uuid NOT NULL,
        "Message" text NOT NULL,
        "DetailJson" text NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CraftAuditEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CraftAuditEvents_CraftSessions_CraftSessionId" FOREIGN KEY ("CraftSessionId") REFERENCES "CraftSessions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "CraftCommands" (
        "Id" uuid NOT NULL,
        "CraftSessionId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "RequestRaw" bytea NOT NULL,
        "ResponseRaw" bytea,
        "CommandName" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CraftCommands" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CraftCommands_CraftSessions_CraftSessionId" FOREIGN KEY ("CraftSessionId") REFERENCES "CraftSessions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "UploadBatches" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid,
        "IdempotencyKey" text NOT NULL,
        "Status" integer NOT NULL,
        "RawPayload" bytea NOT NULL,
        "DecodedMetadataJson" text NOT NULL,
        "RelatedDlogTransactionId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_UploadBatches" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_UploadBatches_DlogTransactions_RelatedDlogTransactionId" FOREIGN KEY ("RelatedDlogTransactionId") REFERENCES "DlogTransactions" ("Id"),
        CONSTRAINT "FK_UploadBatches_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "DownloadBatchItems" (
        "Id" uuid NOT NULL,
        "DownloadBatchId" uuid NOT NULL,
        "TableVersionId" uuid NOT NULL,
        "StepIndex" integer NOT NULL,
        "LastAckStatus" text NOT NULL,
        "Succeeded" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_DownloadBatchItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DownloadBatchItems_DownloadBatches_DownloadBatchId" FOREIGN KEY ("DownloadBatchId") REFERENCES "DownloadBatches" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_DownloadBatchItems_TableVersions_TableVersionId" FOREIGN KEY ("TableVersionId") REFERENCES "TableVersions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "FirmwareUpdateSteps" (
        "Id" uuid NOT NULL,
        "FirmwareUpdateJobId" uuid NOT NULL,
        "StepIndex" integer NOT NULL,
        "Name" text NOT NULL,
        "Succeeded" boolean NOT NULL,
        "Detail" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareUpdateSteps" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FirmwareUpdateSteps_FirmwareUpdateJobs_FirmwareUpdateJobId" FOREIGN KEY ("FirmwareUpdateJobId") REFERENCES "FirmwareUpdateJobs" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE TABLE "UploadRecords" (
        "Id" uuid NOT NULL,
        "UploadBatchId" uuid NOT NULL,
        "RawPayload" bytea NOT NULL,
        "DecodedMetadataJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_UploadRecords" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_UploadRecords_UploadBatches_UploadBatchId" FOREIGN KEY ("UploadBatchId") REFERENCES "UploadBatches" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_BalanceAdjustments_CardAccountId" ON "BalanceAdjustments" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_CallRecords_TerminalId" ON "CallRecords" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_CardAccounts_CardProductId" ON "CardAccounts" ("CardProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_CardAccounts_TerminalId" ON "CardAccounts" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_CraftAuditEvents_CraftSessionId" ON "CraftAuditEvents" ("CraftSessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_CraftCommands_CraftSessionId" ON "CraftCommands" ("CraftSessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_CraftSessions_TerminalId" ON "CraftSessions" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_DlogTransactions_TerminalId" ON "DlogTransactions" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_DownloadBatches_TableSetId" ON "DownloadBatches" ("TableSetId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_DownloadBatches_TerminalId" ON "DownloadBatches" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_DownloadBatchItems_DownloadBatchId" ON "DownloadBatchItems" ("DownloadBatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_DownloadBatchItems_TableVersionId" ON "DownloadBatchItems" ("TableVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_EPurseAccounts_CardAccountId" ON "EPurseAccounts" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_FirmwareTargets_FirmwarePackageId" ON "FirmwareTargets" ("FirmwarePackageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_FirmwareUpdateJobs_FirmwarePackageId" ON "FirmwareUpdateJobs" ("FirmwarePackageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_FirmwareUpdateJobs_TerminalId" ON "FirmwareUpdateJobs" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_FirmwareUpdateSteps_FirmwareUpdateJobId" ON "FirmwareUpdateSteps" ("FirmwareUpdateJobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_NccSessions_TerminalId" ON "NccSessions" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_PaymentTransactions_CardAccountId" ON "PaymentTransactions" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_RatePlans_CustomerId" ON "RatePlans" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_RateRules_RatePlanId" ON "RateRules" ("RatePlanId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_RatingResults_CallRecordId" ON "RatingResults" ("CallRecordId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_Sites_CustomerId" ON "Sites" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TableSets_CustomerId" ON "TableSets" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TableVersions_TableDefinitionId" ON "TableVersions" ("TableDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TableVersions_TableSetId" ON "TableVersions" ("TableSetId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TerminalEvents_TerminalId" ON "TerminalEvents" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TerminalGroups_CustomerId" ON "TerminalGroups" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_Terminals_FirmwareVersionId" ON "Terminals" ("FirmwareVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_Terminals_SiteId" ON "Terminals" ("SiteId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_Terminals_TerminalGroupId" ON "Terminals" ("TerminalGroupId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_Terminals_TransportEndpointId" ON "Terminals" ("TransportEndpointId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TerminalStatusRecords_TerminalId" ON "TerminalStatusRecords" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TerminalTableAssignments_CustomerId" ON "TerminalTableAssignments" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TerminalTableAssignments_SiteId" ON "TerminalTableAssignments" ("SiteId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TerminalTableAssignments_TableSetId" ON "TerminalTableAssignments" ("TableSetId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_TerminalTableAssignments_TerminalId" ON "TerminalTableAssignments" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_UploadBatches_IdempotencyKey" ON "UploadBatches" ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_UploadBatches_RelatedDlogTransactionId" ON "UploadBatches" ("RelatedDlogTransactionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_UploadBatches_TerminalId" ON "UploadBatches" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    CREATE INDEX "IX_UploadRecords_UploadBatchId" ON "UploadRecords" ("UploadBatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502182836_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502182836_InitialCreate', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502190509_AddNccFrameCaptures') THEN
    CREATE TABLE "NccFrameCaptures" (
        "Id" uuid NOT NULL,
        "OriginalFileName" text NOT NULL,
        "ByteLength" integer NOT NULL,
        "RawBytes" bytea NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_NccFrameCaptures" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502190509_AddNccFrameCaptures') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502190509_AddNccFrameCaptures', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    ALTER TABLE "DlogTransactions" ADD "Direction" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    ALTER TABLE "DlogTransactions" ADD "IdempotencyKey" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    ALTER TABLE "DlogTransactions" ADD "NccSessionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    ALTER TABLE "DlogTransactions" ADD "ProcessingStatus" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    ALTER TABLE "DlogReplayRequests" ADD "ResultSummaryJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    CREATE TABLE "DlogCorrelationLinks" (
        "Id" uuid NOT NULL,
        "RequestTransactionId" uuid NOT NULL,
        "ResponseTransactionId" uuid NOT NULL,
        "LinkRule" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_DlogCorrelationLinks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DlogCorrelationLinks_DlogTransactions_RequestTransactionId" FOREIGN KEY ("RequestTransactionId") REFERENCES "DlogTransactions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_DlogCorrelationLinks_DlogTransactions_ResponseTransactionId" FOREIGN KEY ("ResponseTransactionId") REFERENCES "DlogTransactions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    CREATE TABLE "DlogMessageTypes" (
        "MtCode" integer NOT NULL,
        "SymbolName" text NOT NULL,
        "MessageAction" text NOT NULL,
        "ImmediateClear" boolean NOT NULL,
        "Notes" text,
        CONSTRAINT "PK_DlogMessageTypes" PRIMARY KEY ("MtCode")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    CREATE TABLE "DlogParseDiagnostics" (
        "Id" uuid NOT NULL,
        "DlogTransactionId" uuid NOT NULL,
        "Severity" text NOT NULL,
        "Code" text NOT NULL,
        "Message" text NOT NULL,
        "Detail" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_DlogParseDiagnostics" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DlogParseDiagnostics_DlogTransactions_DlogTransactionId" FOREIGN KEY ("DlogTransactionId") REFERENCES "DlogTransactions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    CREATE UNIQUE INDEX "IX_DlogTransactions_IdempotencyKey" ON "DlogTransactions" ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL AND "IdempotencyKey" <> '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    CREATE INDEX "IX_DlogTransactions_NccSessionId" ON "DlogTransactions" ("NccSessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    CREATE INDEX "IX_DlogCorrelationLinks_RequestTransactionId" ON "DlogCorrelationLinks" ("RequestTransactionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    CREATE INDEX "IX_DlogCorrelationLinks_ResponseTransactionId" ON "DlogCorrelationLinks" ("ResponseTransactionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    CREATE INDEX "IX_DlogParseDiagnostics_DlogTransactionId" ON "DlogParseDiagnostics" ("DlogTransactionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    ALTER TABLE "DlogTransactions" ADD CONSTRAINT "FK_DlogTransactions_NccSessions_NccSessionId" FOREIGN KEY ("NccSessionId") REFERENCES "NccSessions" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502194017_AddDlogTranche3') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502194017_AddDlogTranche3', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TerminalTableAssignments" ADD "AssignedAtUtc" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '-infinity';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TerminalTableAssignments" ADD "PreviousTableSetId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableVersions" ADD "DependsOnTableDefinitionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableVersions" ADD "PayloadSha256Hex" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableVersions" ADD "SortOrder" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableVersions" ADD "TablePayloadId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableVersions" ADD "ValidationDiagnosticsJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableVersions" ADD "ValidationPassed" boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableSets" ADD "PublishGeneration" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableSets" ADD "PublishedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableSets" ADD "Status" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "DownloadBatchItems" ADD "ErrorDetail" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "DownloadBatchItems" ADD "ItemStatus" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "DownloadBatches" ADD "CompletedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "DownloadBatches" ADD "DiagnosticsJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "DownloadBatches" ADD "LastError" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "DownloadBatches" ADD "PartialDefinitionIdsJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "DownloadBatches" ADD "RetryCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "DownloadBatches" ADD "Scope" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE TABLE "CustomerTableOverrides" (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "TableDefinitionId" uuid NOT NULL,
        "TableVersionId" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CustomerTableOverrides" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CustomerTableOverrides_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_CustomerTableOverrides_TableDefinitions_TableDefinitionId" FOREIGN KEY ("TableDefinitionId") REFERENCES "TableDefinitions" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_CustomerTableOverrides_TableVersions_TableVersionId" FOREIGN KEY ("TableVersionId") REFERENCES "TableVersions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE TABLE "SiteTableOverrides" (
        "Id" uuid NOT NULL,
        "SiteId" uuid NOT NULL,
        "TableDefinitionId" uuid NOT NULL,
        "TableVersionId" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_SiteTableOverrides" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SiteTableOverrides_Sites_SiteId" FOREIGN KEY ("SiteId") REFERENCES "Sites" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SiteTableOverrides_TableDefinitions_TableDefinitionId" FOREIGN KEY ("TableDefinitionId") REFERENCES "TableDefinitions" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SiteTableOverrides_TableVersions_TableVersionId" FOREIGN KEY ("TableVersionId") REFERENCES "TableVersions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE TABLE "TablePayloads" (
        "Id" uuid NOT NULL,
        "RawContent" bytea NOT NULL,
        "Sha256Hex" text NOT NULL,
        "LengthBytes" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TablePayloads" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE TABLE "TerminalTableOverrides" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "TableDefinitionId" uuid NOT NULL,
        "TableVersionId" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalTableOverrides" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalTableOverrides_TableDefinitions_TableDefinitionId" FOREIGN KEY ("TableDefinitionId") REFERENCES "TableDefinitions" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_TerminalTableOverrides_TableVersions_TableVersionId" FOREIGN KEY ("TableVersionId") REFERENCES "TableVersions" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_TerminalTableOverrides_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE INDEX "IX_TerminalTableAssignments_PreviousTableSetId" ON "TerminalTableAssignments" ("PreviousTableSetId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE INDEX "IX_TableVersions_TablePayloadId" ON "TableVersions" ("TablePayloadId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE UNIQUE INDEX "IX_CustomerTableOverrides_CustomerId_TableDefinitionId" ON "CustomerTableOverrides" ("CustomerId", "TableDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE INDEX "IX_CustomerTableOverrides_TableDefinitionId" ON "CustomerTableOverrides" ("TableDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE INDEX "IX_CustomerTableOverrides_TableVersionId" ON "CustomerTableOverrides" ("TableVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE UNIQUE INDEX "IX_SiteTableOverrides_SiteId_TableDefinitionId" ON "SiteTableOverrides" ("SiteId", "TableDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE INDEX "IX_SiteTableOverrides_TableDefinitionId" ON "SiteTableOverrides" ("TableDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE INDEX "IX_SiteTableOverrides_TableVersionId" ON "SiteTableOverrides" ("TableVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE INDEX "IX_TerminalTableOverrides_TableDefinitionId" ON "TerminalTableOverrides" ("TableDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE INDEX "IX_TerminalTableOverrides_TableVersionId" ON "TerminalTableOverrides" ("TableVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    CREATE UNIQUE INDEX "IX_TerminalTableOverrides_TerminalId_TableDefinitionId" ON "TerminalTableOverrides" ("TerminalId", "TableDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TableVersions" ADD CONSTRAINT "FK_TableVersions_TablePayloads_TablePayloadId" FOREIGN KEY ("TablePayloadId") REFERENCES "TablePayloads" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    ALTER TABLE "TerminalTableAssignments" ADD CONSTRAINT "FK_TerminalTableAssignments_TableSets_PreviousTableSetId" FOREIGN KEY ("PreviousTableSetId") REFERENCES "TableSets" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    UPDATE "TableVersions" SET "ValidationPassed" = false WHERE "Payload" IS NULL OR octet_length("Payload") = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502200409_AddTableDistributionTranche4') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502200409_AddTableDistributionTranche4', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502201146_TerminalTableAssignmentUnique') THEN
    DROP INDEX "IX_TerminalTableAssignments_TerminalId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502201146_TerminalTableAssignmentUnique') THEN
    CREATE UNIQUE INDEX "IX_TerminalTableAssignments_TerminalId" ON "TerminalTableAssignments" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502201146_TerminalTableAssignmentUnique') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502201146_TerminalTableAssignmentUnique', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" DROP CONSTRAINT "FK_RateRules_RatePlans_RatePlanId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    DROP INDEX "IX_RateRules_RatePlanId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE TABLE "RatePlanVersions" (
        "Id" uuid NOT NULL,
        "RatePlanId" uuid NOT NULL,
        "VersionNumber" integer NOT NULL,
        "Status" integer NOT NULL,
        "PublishedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_RatePlanVersions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RatePlanVersions_RatePlans_RatePlanId" FOREIGN KEY ("RatePlanId") REFERENCES "RatePlans" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    INSERT INTO "RatePlanVersions" ("Id","RatePlanId","VersionNumber","Status","PublishedAtUtc","CreatedAtUtc","UpdatedAtUtc","CreatedBy","UpdatedBy","Version")
    SELECT gen_random_uuid(), rp."Id", 1, 0, NULL, NOW() AT TIME ZONE 'UTC', NOW() AT TIME ZONE 'UTC', 'migration', 'migration', 0
    FROM "RatePlans" rp;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" ADD "RatePlanVersionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    UPDATE "RateRules" rr
    SET "RatePlanVersionId" = v."Id"
    FROM "RatePlanVersions" v
    WHERE v."RatePlanId" = rr."RatePlanId" AND v."VersionNumber" = 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    DELETE FROM "RateRules" WHERE "RatePlanVersionId" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" DROP COLUMN "RatePlanId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" ALTER COLUMN "RatePlanVersionId" SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RatingResults" ADD "DecisionKind" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RatingResults" ADD "DeterminismInputJson" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RatingResults" ADD "RatePlanVersionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" ADD "MatchKind" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" ADD "Outcome" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" ADD "Pattern" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" ADD "Priority" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" ADD "RatePerMinuteUsd" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RatePlans" ADD "PublishedVersionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "DialedNumberClasses" ADD "CustomerId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "DialedNumberClasses" ADD "IsBlocked" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "DialedNumberClasses" ADD "IsEmergency" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "DialedNumberClasses" ADD "IsFree" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "DialedNumberClasses" ADD "MatchKind" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "DialedNumberClasses" ADD "SortOrder" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "CallRecords" ADD "AppliedRatePlanVersionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "CallRecords" ADD "Reconciliation" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE TABLE "CallAuthorizationRequests" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid,
        "RatePlanId" uuid,
        "DialedDigits" text NOT NULL,
        "AvailableBalanceUsd" numeric,
        "RequestPayloadJson" text NOT NULL,
        "DecisionPayloadJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CallAuthorizationRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CallAuthorizationRequests_RatePlans_RatePlanId" FOREIGN KEY ("RatePlanId") REFERENCES "RatePlans" ("Id"),
        CONSTRAINT "FK_CallAuthorizationRequests_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE TABLE "CallChargeSegments" (
        "Id" uuid NOT NULL,
        "RatingResultId" uuid NOT NULL,
        "SegmentIndex" integer NOT NULL,
        "Label" text NOT NULL,
        "AmountUsd" numeric NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CallChargeSegments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CallChargeSegments_RatingResults_RatingResultId" FOREIGN KEY ("RatingResultId") REFERENCES "RatingResults" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE TABLE "RatingDiagnostics" (
        "Id" uuid NOT NULL,
        "RatingResultId" uuid NOT NULL,
        "Code" text NOT NULL,
        "Severity" text NOT NULL,
        "Message" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_RatingDiagnostics" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RatingDiagnostics_RatingResults_RatingResultId" FOREIGN KEY ("RatingResultId") REFERENCES "RatingResults" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE TABLE "Tariffs" (
        "Id" uuid NOT NULL,
        "RatePlanVersionId" uuid NOT NULL,
        "Name" text NOT NULL,
        "RatePerMinuteUsd" numeric NOT NULL,
        "Notes" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_Tariffs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Tariffs_RatePlanVersions_RatePlanVersionId" FOREIGN KEY ("RatePlanVersionId") REFERENCES "RatePlanVersions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE TABLE "DestinationPrefixes" (
        "Id" uuid NOT NULL,
        "RatePlanVersionId" uuid NOT NULL,
        "PrefixDigits" text NOT NULL,
        "TariffId" uuid,
        "Notes" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_DestinationPrefixes" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DestinationPrefixes_RatePlanVersions_RatePlanVersionId" FOREIGN KEY ("RatePlanVersionId") REFERENCES "RatePlanVersions" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_DestinationPrefixes_Tariffs_TariffId" FOREIGN KEY ("TariffId") REFERENCES "Tariffs" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE TABLE "TimeBands" (
        "Id" uuid NOT NULL,
        "RatePlanVersionId" uuid NOT NULL,
        "DayOfWeekMask" integer NOT NULL,
        "StartMinuteOfDay" integer NOT NULL,
        "EndMinuteOfDay" integer NOT NULL,
        "TariffId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TimeBands" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TimeBands_RatePlanVersions_RatePlanVersionId" FOREIGN KEY ("RatePlanVersionId") REFERENCES "RatePlanVersions" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_TimeBands_Tariffs_TariffId" FOREIGN KEY ("TariffId") REFERENCES "Tariffs" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_RatingResults_RatePlanVersionId" ON "RatingResults" ("RatePlanVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_RatePlans_PublishedVersionId" ON "RatePlans" ("PublishedVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_DialedNumberClasses_CustomerId" ON "DialedNumberClasses" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_CallRecords_AppliedRatePlanVersionId" ON "CallRecords" ("AppliedRatePlanVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_CallAuthorizationRequests_RatePlanId" ON "CallAuthorizationRequests" ("RatePlanId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_CallAuthorizationRequests_TerminalId" ON "CallAuthorizationRequests" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_CallChargeSegments_RatingResultId" ON "CallChargeSegments" ("RatingResultId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_DestinationPrefixes_RatePlanVersionId" ON "DestinationPrefixes" ("RatePlanVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_DestinationPrefixes_TariffId" ON "DestinationPrefixes" ("TariffId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE UNIQUE INDEX "IX_RatePlanVersions_RatePlanId_VersionNumber" ON "RatePlanVersions" ("RatePlanId", "VersionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_RateRules_RatePlanVersionId" ON "RateRules" ("RatePlanVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_RatingDiagnostics_RatingResultId" ON "RatingDiagnostics" ("RatingResultId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_Tariffs_RatePlanVersionId" ON "Tariffs" ("RatePlanVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_TimeBands_RatePlanVersionId" ON "TimeBands" ("RatePlanVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    CREATE INDEX "IX_TimeBands_TariffId" ON "TimeBands" ("TariffId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "CallRecords" ADD CONSTRAINT "FK_CallRecords_RatePlanVersions_AppliedRatePlanVersionId" FOREIGN KEY ("AppliedRatePlanVersionId") REFERENCES "RatePlanVersions" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "DialedNumberClasses" ADD CONSTRAINT "FK_DialedNumberClasses_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RatePlans" ADD CONSTRAINT "FK_RatePlans_RatePlanVersions_PublishedVersionId" FOREIGN KEY ("PublishedVersionId") REFERENCES "RatePlanVersions" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RateRules" ADD CONSTRAINT "FK_RateRules_RatePlanVersions_RatePlanVersionId" FOREIGN KEY ("RatePlanVersionId") REFERENCES "RatePlanVersions" ("Id") ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    ALTER TABLE "RatingResults" ADD CONSTRAINT "FK_RatingResults_RatePlanVersions_RatePlanVersionId" FOREIGN KEY ("RatePlanVersionId") REFERENCES "RatePlanVersions" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502204325_Tranche5RatingAndCallRecords') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502204325_Tranche5RatingAndCallRecords', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "SmartcardTypes" ADD "Code" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "SmartcardTypes" ADD "MapsToCardType" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "SmartcardTypes" ADD "Notes" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "PaymentTransactions" ADD "RawPayloadJson" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "PaymentTransactions" ADD "ReportedCardType" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "CardProducts" ADD "AllowNegativeBalance" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "CardProducts" ADD "DefaultCardType" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "CardProducts" ADD "IsTestFixtureCatalogEntry" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "CardAccounts" ADD "CredentialKind" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "CardAccounts" ADD "CredentialTokenRef" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    ALTER TABLE "CardAccounts" ADD "ResolvedCardType" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE TABLE "CardBalances" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid NOT NULL,
        "Amount" numeric NOT NULL,
        "Currency" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CardBalances" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CardBalances_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE TABLE "CardCredentials" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid NOT NULL,
        "TokenReference" text NOT NULL,
        "Kind" integer NOT NULL,
        "Active" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CardCredentials" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CardCredentials_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE TABLE "CardReadEvents" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid,
        "TerminalId" uuid,
        "ReportedCardType" integer NOT NULL,
        "RawPayloadJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CardReadEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CardReadEvents_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id"),
        CONSTRAINT "FK_CardReadEvents_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE TABLE "CardReconciliationBatches" (
        "Id" uuid NOT NULL,
        "Status" integer NOT NULL,
        "DetailJson" text NOT NULL,
        "PostedAtUtc" timestamp with time zone,
        "ClosedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CardReconciliationBatches" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE TABLE "CardWriteEvents" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid,
        "IntendedOperation" text NOT NULL,
        "RawPayloadJson" text NOT NULL,
        "Disposition" integer NOT NULL,
        "SimulationMode" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CardWriteEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CardWriteEvents_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE TABLE "EPurseProfiles" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid NOT NULL,
        "ProfileJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_EPurseProfiles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_EPurseProfiles_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE TABLE "SmartcardProfiles" (
        "Id" uuid NOT NULL,
        "CardAccountId" uuid NOT NULL,
        "SmartcardTypeId" uuid,
        "ProfileJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_SmartcardProfiles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SmartcardProfiles_CardAccounts_CardAccountId" FOREIGN KEY ("CardAccountId") REFERENCES "CardAccounts" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SmartcardProfiles_SmartcardTypes_SmartcardTypeId" FOREIGN KEY ("SmartcardTypeId") REFERENCES "SmartcardTypes" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE UNIQUE INDEX "IX_CardBalances_CardAccountId" ON "CardBalances" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE INDEX "IX_CardCredentials_CardAccountId" ON "CardCredentials" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE INDEX "IX_CardReadEvents_CardAccountId" ON "CardReadEvents" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE INDEX "IX_CardReadEvents_TerminalId" ON "CardReadEvents" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE INDEX "IX_CardWriteEvents_CardAccountId" ON "CardWriteEvents" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE UNIQUE INDEX "IX_EPurseProfiles_CardAccountId" ON "EPurseProfiles" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE UNIQUE INDEX "IX_SmartcardProfiles_CardAccountId" ON "SmartcardProfiles" ("CardAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    CREATE INDEX "IX_SmartcardProfiles_SmartcardTypeId" ON "SmartcardProfiles" ("SmartcardTypeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502212615_Tranche6CardPaymentsSkeleton') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502212615_Tranche6CardPaymentsSkeleton', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    ALTER TABLE "CraftSessions" ADD "FieldNotes" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    ALTER TABLE "CraftCommands" ADD "AuditReason" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    ALTER TABLE "CraftCommands" ADD "CraftCommandTypeId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    ALTER TABLE "CraftCommands" ADD "DestructiveConfirmed" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    ALTER TABLE "CraftCommands" ADD "SimulationExecution" boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE TABLE "CdrUploadRequests" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "SimulationMode" boolean NOT NULL,
        "DetailJson" text NOT NULL,
        "CraftSessionId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CdrUploadRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CdrUploadRequests_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE TABLE "CraftCommandTypes" (
        "Id" uuid NOT NULL,
        "Code" text NOT NULL,
        "DisplayName" text NOT NULL,
        "IsDestructive" boolean NOT NULL,
        "DefaultSimulationOnly" boolean NOT NULL,
        "Notes" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CraftCommandTypes" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE TABLE "CraftDiagnostics" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "CraftSessionId" uuid,
        "Category" text NOT NULL,
        "PayloadJson" text NOT NULL,
        "RecordedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CraftDiagnostics" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CraftDiagnostics_CraftSessions_CraftSessionId" FOREIGN KEY ("CraftSessionId") REFERENCES "CraftSessions" ("Id"),
        CONSTRAINT "FK_CraftDiagnostics_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE TABLE "RemoteTableReloadRequests" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "SimulationMode" boolean NOT NULL,
        "DetailJson" text NOT NULL,
        "CraftSessionId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_RemoteTableReloadRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RemoteTableReloadRequests_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE TABLE "TerminalDiagnosticSnapshots" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "SnapshotJson" text NOT NULL,
        "Source" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalDiagnosticSnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalDiagnosticSnapshots_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE INDEX "IX_CraftCommands_CraftCommandTypeId" ON "CraftCommands" ("CraftCommandTypeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE INDEX "IX_CdrUploadRequests_TerminalId" ON "CdrUploadRequests" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE UNIQUE INDEX "IX_CraftCommandTypes_Code" ON "CraftCommandTypes" ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE INDEX "IX_CraftDiagnostics_CraftSessionId" ON "CraftDiagnostics" ("CraftSessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE INDEX "IX_CraftDiagnostics_TerminalId" ON "CraftDiagnostics" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE INDEX "IX_RemoteTableReloadRequests_TerminalId" ON "RemoteTableReloadRequests" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    CREATE INDEX "IX_TerminalDiagnosticSnapshots_TerminalId" ON "TerminalDiagnosticSnapshots" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    ALTER TABLE "CraftCommands" ADD CONSTRAINT "FK_CraftCommands_CraftCommandTypes_CraftCommandTypeId" FOREIGN KEY ("CraftCommandTypeId") REFERENCES "CraftCommandTypes" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502214857_Tranche7CraftFieldOperations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502214857_Tranche7CraftFieldOperations', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwareUpdateSteps" ADD "StepStatus" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwareUpdateJobs" ADD "ApprovedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwareUpdateJobs" ADD "ApprovedByOperatorId" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwareUpdateJobs" ADD "CancelReason" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwareUpdateJobs" ADD "FirmwareArtifactId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwareTargets" ADD "Notes" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwarePackages" ADD "PrimaryArtifactId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE TABLE "FirmwareArtifacts" (
        "Id" uuid NOT NULL,
        "FirmwarePackageId" uuid NOT NULL,
        "Kind" text NOT NULL,
        "Sha256Hex" text NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "StorageRef" text NOT NULL,
        "MetadataJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareArtifacts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FirmwareArtifacts_FirmwarePackages_FirmwarePackageId" FOREIGN KEY ("FirmwarePackageId") REFERENCES "FirmwarePackages" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE TABLE "FirmwareCompatibilityRules" (
        "Id" uuid NOT NULL,
        "FirmwarePackageId" uuid NOT NULL,
        "RequiredTerminalFirmwareVersionId" uuid,
        "RequiredTargetSkuContains" text,
        "Notes" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareCompatibilityRules" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FirmwareCompatibilityRules_FirmwarePackages_FirmwarePackage~" FOREIGN KEY ("FirmwarePackageId") REFERENCES "FirmwarePackages" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_FirmwareCompatibilityRules_FirmwareVersions_RequiredTermina~" FOREIGN KEY ("RequiredTerminalFirmwareVersionId") REFERENCES "FirmwareVersions" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE TABLE "FirmwareRollBackPlans" (
        "Id" uuid NOT NULL,
        "FirmwareUpdateJobId" uuid NOT NULL,
        "BackupNotes" text NOT NULL,
        "RecoveryStepsJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareRollBackPlans" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FirmwareRollBackPlans_FirmwareUpdateJobs_FirmwareUpdateJobId" FOREIGN KEY ("FirmwareUpdateJobId") REFERENCES "FirmwareUpdateJobs" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE TABLE "FirmwareUpdateSafetyChecks" (
        "Id" uuid NOT NULL,
        "FirmwareUpdateJobId" uuid NOT NULL,
        "Code" text NOT NULL,
        "Passed" boolean NOT NULL,
        "DetailJson" text NOT NULL,
        "EvaluatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareUpdateSafetyChecks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FirmwareUpdateSafetyChecks_FirmwareUpdateJobs_FirmwareUpdat~" FOREIGN KEY ("FirmwareUpdateJobId") REFERENCES "FirmwareUpdateJobs" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE TABLE "FirmwareBlockManifests" (
        "Id" uuid NOT NULL,
        "FirmwarePackageId" uuid NOT NULL,
        "FirmwareArtifactId" uuid,
        "LayoutJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FirmwareBlockManifests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FirmwareBlockManifests_FirmwareArtifacts_FirmwareArtifactId" FOREIGN KEY ("FirmwareArtifactId") REFERENCES "FirmwareArtifacts" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_FirmwareBlockManifests_FirmwarePackages_FirmwarePackageId" FOREIGN KEY ("FirmwarePackageId") REFERENCES "FirmwarePackages" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE INDEX "IX_FirmwareUpdateJobs_FirmwareArtifactId" ON "FirmwareUpdateJobs" ("FirmwareArtifactId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE INDEX "IX_FirmwarePackages_PrimaryArtifactId" ON "FirmwarePackages" ("PrimaryArtifactId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE INDEX "IX_FirmwareArtifacts_FirmwarePackageId" ON "FirmwareArtifacts" ("FirmwarePackageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE INDEX "IX_FirmwareBlockManifests_FirmwareArtifactId" ON "FirmwareBlockManifests" ("FirmwareArtifactId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE INDEX "IX_FirmwareBlockManifests_FirmwarePackageId" ON "FirmwareBlockManifests" ("FirmwarePackageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE INDEX "IX_FirmwareCompatibilityRules_FirmwarePackageId" ON "FirmwareCompatibilityRules" ("FirmwarePackageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE INDEX "IX_FirmwareCompatibilityRules_RequiredTerminalFirmwareVersionId" ON "FirmwareCompatibilityRules" ("RequiredTerminalFirmwareVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE UNIQUE INDEX "IX_FirmwareRollBackPlans_FirmwareUpdateJobId" ON "FirmwareRollBackPlans" ("FirmwareUpdateJobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    CREATE INDEX "IX_FirmwareUpdateSafetyChecks_FirmwareUpdateJobId" ON "FirmwareUpdateSafetyChecks" ("FirmwareUpdateJobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwarePackages" ADD CONSTRAINT "FK_FirmwarePackages_FirmwareArtifacts_PrimaryArtifactId" FOREIGN KEY ("PrimaryArtifactId") REFERENCES "FirmwareArtifacts" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    ALTER TABLE "FirmwareUpdateJobs" ADD CONSTRAINT "FK_FirmwareUpdateJobs_FirmwareArtifacts_FirmwareArtifactId" FOREIGN KEY ("FirmwareArtifactId") REFERENCES "FirmwareArtifacts" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502222110_Tranche8FirmwareRegistry') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502222110_Tranche8FirmwareRegistry', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502234611_Tranche10ProductionReadiness') THEN
    DROP INDEX "IX_FirmwareUpdateJobs_TerminalId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502234611_Tranche10ProductionReadiness') THEN
    ALTER TABLE "DownloadBatches" ADD "ClientIdempotencyKey" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502234611_Tranche10ProductionReadiness') THEN
    CREATE TABLE "SubsystemHeartbeats" (
        "Subsystem" character varying(64) NOT NULL,
        "LastSeenUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_SubsystemHeartbeats" PRIMARY KEY ("Subsystem")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502234611_Tranche10ProductionReadiness') THEN
    CREATE INDEX "IX_FirmwareUpdateJobs_TerminalId_Status" ON "FirmwareUpdateJobs" ("TerminalId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502234611_Tranche10ProductionReadiness') THEN
    CREATE UNIQUE INDEX "IX_DownloadBatches_ClientIdempotencyKey" ON "DownloadBatches" ("ClientIdempotencyKey") WHERE "ClientIdempotencyKey" IS NOT NULL AND "ClientIdempotencyKey" <> '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502234611_Tranche10ProductionReadiness') THEN
    CREATE INDEX "IX_AuditEvents_CreatedAtUtc" ON "AuditEvents" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502234611_Tranche10ProductionReadiness') THEN
    CREATE INDEX "IX_AuditEvents_TerminalId_CreatedAtUtc" ON "AuditEvents" ("TerminalId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502234611_Tranche10ProductionReadiness') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502234611_Tranche10ProductionReadiness', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503010027_CapturedHardwareSessions') THEN
    CREATE TABLE "CapturedHardwareSessions" (
        "Id" uuid NOT NULL,
        "SchemaVersion" integer NOT NULL,
        "SessionKind" text NOT NULL,
        "TerminalId" uuid,
        "SourceLabel" text NOT NULL,
        "EnvelopeJson" text NOT NULL,
        "EnvelopeChecksumSha256" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CapturedHardwareSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CapturedHardwareSessions_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503010027_CapturedHardwareSessions') THEN
    CREATE INDEX "IX_CapturedHardwareSessions_CreatedAtUtc" ON "CapturedHardwareSessions" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503010027_CapturedHardwareSessions') THEN
    CREATE INDEX "IX_CapturedHardwareSessions_EnvelopeChecksumSha256" ON "CapturedHardwareSessions" ("EnvelopeChecksumSha256");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503010027_CapturedHardwareSessions') THEN
    CREATE INDEX "IX_CapturedHardwareSessions_SessionKind" ON "CapturedHardwareSessions" ("SessionKind");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503010027_CapturedHardwareSessions') THEN
    CREATE INDEX "IX_CapturedHardwareSessions_TerminalId" ON "CapturedHardwareSessions" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503010027_CapturedHardwareSessions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260503010027_CapturedHardwareSessions', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503034129_HostDownloadPhaseColumn') THEN
    ALTER TABLE "DownloadBatchItems" ADD "HostDownloadPhase" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503034129_HostDownloadPhaseColumn') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260503034129_HostDownloadPhaseColumn', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503072310_NccSessionLifecycleStatus') THEN
    ALTER TABLE "NccSessions" ADD "Status" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503072310_NccSessionLifecycleStatus') THEN
    UPDATE "NccSessions" SET "Status" = 1 WHERE "EndedAtUtc" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260503072310_NccSessionLifecycleStatus') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260503072310_NccSessionLifecycleStatus', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123130622_NccSessionFramesAndCallPayloads') THEN
    ALTER TABLE "CallRecords" ADD "RequestRaw" bytea NOT NULL DEFAULT BYTEA E'\\x';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123130622_NccSessionFramesAndCallPayloads') THEN
    ALTER TABLE "CallRecords" ADD "ResponseRaw" bytea NOT NULL DEFAULT BYTEA E'\\x';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123130622_NccSessionFramesAndCallPayloads') THEN
    CREATE TABLE "NccSessionFrames" (
        "Id" uuid NOT NULL,
        "NccSessionId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "TerminalIdHex" text NOT NULL,
        "TerminalMatched" boolean NOT NULL,
        "RawBytes" bytea NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_NccSessionFrames" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_NccSessionFrames_NccSessions_NccSessionId" FOREIGN KEY ("NccSessionId") REFERENCES "NccSessions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123130622_NccSessionFramesAndCallPayloads') THEN
    CREATE INDEX "IX_NccSessionFrames_NccSessionId_Sequence" ON "NccSessionFrames" ("NccSessionId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123130622_NccSessionFramesAndCallPayloads') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261123130622_NccSessionFramesAndCallPayloads', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123140456_OperatorAccounts') THEN
    CREATE TABLE "OperatorAccounts" (
        "Id" uuid NOT NULL,
        "Username" character varying(64) NOT NULL,
        "DisplayName" character varying(120) NOT NULL,
        "PasswordHash" text NOT NULL,
        "Role" integer NOT NULL,
        "Enabled" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_OperatorAccounts" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123140456_OperatorAccounts') THEN
    CREATE UNIQUE INDEX "IX_OperatorAccounts_Username" ON "OperatorAccounts" ("Username");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123140456_OperatorAccounts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261123140456_OperatorAccounts', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124045139_TerminalGroupMemberships') THEN
    CREATE TABLE "TerminalGroupMemberships" (
        "TerminalId" uuid NOT NULL,
        "TerminalGroupId" uuid NOT NULL,
        CONSTRAINT "PK_TerminalGroupMemberships" PRIMARY KEY ("TerminalId", "TerminalGroupId"),
        CONSTRAINT "FK_TerminalGroupMemberships_TerminalGroups_TerminalGroupId" FOREIGN KEY ("TerminalGroupId") REFERENCES "TerminalGroups" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TerminalGroupMemberships_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124045139_TerminalGroupMemberships') THEN
    CREATE INDEX "IX_TerminalGroupMemberships_TerminalGroupId" ON "TerminalGroupMemberships" ("TerminalGroupId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124045139_TerminalGroupMemberships') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124045139_TerminalGroupMemberships', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124050650_ScheduledTableDownloads') THEN
    CREATE TABLE "ScheduledTableDownloads" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "TableSetId" uuid NOT NULL,
        "AvailableAtUtc" timestamp with time zone NOT NULL,
        "NccSessionId" uuid,
        "LastBatchId" uuid,
        "StartedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "CancelledAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_ScheduledTableDownloads" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ScheduledTableDownloads_DownloadBatches_LastBatchId" FOREIGN KEY ("LastBatchId") REFERENCES "DownloadBatches" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_ScheduledTableDownloads_NccSessions_NccSessionId" FOREIGN KEY ("NccSessionId") REFERENCES "NccSessions" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_ScheduledTableDownloads_TableSets_TableSetId" FOREIGN KEY ("TableSetId") REFERENCES "TableSets" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ScheduledTableDownloads_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124050650_ScheduledTableDownloads') THEN
    CREATE INDEX "IX_ScheduledTableDownloads_LastBatchId" ON "ScheduledTableDownloads" ("LastBatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124050650_ScheduledTableDownloads') THEN
    CREATE INDEX "IX_ScheduledTableDownloads_NccSessionId" ON "ScheduledTableDownloads" ("NccSessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124050650_ScheduledTableDownloads') THEN
    CREATE INDEX "IX_ScheduledTableDownloads_TableSetId_AvailableAtUtc" ON "ScheduledTableDownloads" ("TableSetId", "AvailableAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124050650_ScheduledTableDownloads') THEN
    CREATE UNIQUE INDEX "IX_ScheduledTableDownloads_TerminalId" ON "ScheduledTableDownloads" ("TerminalId") WHERE "CompletedAtUtc" IS NULL AND "CancelledAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124050650_ScheduledTableDownloads') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124050650_ScheduledTableDownloads', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124052644_FirmwareTableTargets') THEN
    ALTER TABLE "TableSets" ADD "TargetFirmwareVersionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124052644_FirmwareTableTargets') THEN
    ALTER TABLE "FirmwareVersions" ADD "TableProfileId" character varying(48);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124052644_FirmwareTableTargets') THEN
    CREATE INDEX "IX_TableSets_TargetFirmwareVersionId" ON "TableSets" ("TargetFirmwareVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124052644_FirmwareTableTargets') THEN
    ALTER TABLE "TableSets" ADD CONSTRAINT "FK_TableSets_FirmwareVersions_TargetFirmwareVersionId" FOREIGN KEY ("TargetFirmwareVersionId") REFERENCES "FirmwareVersions" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124052644_FirmwareTableTargets') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124052644_FirmwareTableTargets', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124095532_TerminalFamilyInventory') THEN
    ALTER TABLE "Terminals" ADD "TerminalFamilyId" character varying(48);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124095532_TerminalFamilyInventory') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124095532_TerminalFamilyInventory', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124101318_InmateFeatureConfigurations') THEN
    CREATE TABLE "InmateFeatureConfigurations" (
        "Id" uuid NOT NULL,
        "Name" character varying(6) NOT NULL,
        "NameKey" character varying(24) NOT NULL,
        "Description" character varying(80) NOT NULL,
        "SettingsJson" jsonb NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_InmateFeatureConfigurations" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124101318_InmateFeatureConfigurations') THEN
    CREATE UNIQUE INDEX "IX_InmateFeatureConfigurations_NameKey" ON "InmateFeatureConfigurations" ("NameKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124101318_InmateFeatureConfigurations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124101318_InmateFeatureConfigurations', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124120729_TerminalCallbackRequests') THEN
    CREATE TABLE "TerminalCallbackRequests" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "RequestedAtUtc" timestamp with time zone NOT NULL,
        "DueAtUtc" timestamp with time zone,
        "TerminalClockOffsetMinutes" integer NOT NULL,
        "DeliverAssignedTables" boolean NOT NULL,
        "AcknowledgedAtUtc" timestamp with time zone,
        "ProgrammedForUtc" timestamp with time zone,
        "AcknowledgedSessionId" uuid,
        "ReturnedAtUtc" timestamp with time zone,
        "CancelledAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalCallbackRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalCallbackRequests_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124120729_TerminalCallbackRequests') THEN
    CREATE UNIQUE INDEX "IX_TerminalCallbackRequests_TerminalId" ON "TerminalCallbackRequests" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124120729_TerminalCallbackRequests') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124120729_TerminalCallbackRequests', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124123642_TerminalInventories') THEN
    CREATE TABLE "TerminalInventories" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "Details_RegionCode" character varying(32),
        "Details_Route" character varying(24),
        "Details_Stop" character varying(24),
        "Details_SiteDescription" character varying(120),
        "Details_AddressLine1" character varying(120),
        "Details_AddressLine2" character varying(120),
        "Details_AddressLine3" character varying(120),
        "Details_Contact" character varying(80),
        "Details_Indoor" boolean NOT NULL,
        "Details_Basement" boolean NOT NULL,
        "Details_ServiceClass" character varying(24),
        "Details_PublicService" boolean NOT NULL,
        "Details_SalespersonCode" text,
        "Details_RevenueAccountingCode" text,
        "Details_BillingCustomerCode" text,
        "Details_VoucherAccountCode" text,
        "Details_IndustryCode" text,
        "Details_LocationGroupCode" text,
        "Details_EquipmentCost" numeric(12,2),
        "Details_LoopMileageCost" numeric(12,2),
        "Details_HousingCode" character varying(24),
        "Details_DirectoryCode" character varying(24),
        "Details_DirectoryCount" integer,
        "Details_SignCode" character varying(24),
        "Details_SignCount" integer,
        "Details_ExtensionCode" character varying(24),
        "Details_ExtensionCount" integer,
        "Details_MiscellaneousCode" character varying(24),
        "Details_MiscellaneousCount" integer,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalInventories" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalInventories_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124123642_TerminalInventories') THEN
    CREATE UNIQUE INDEX "IX_TerminalInventories_TerminalId" ON "TerminalInventories" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124123642_TerminalInventories') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124123642_TerminalInventories', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124130248_ScheduledRecordSelection') THEN
    ALTER TABLE "ScheduledTableDownloads" ADD "RecordNumbers" integer[];
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124130248_ScheduledRecordSelection') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124130248_ScheduledRecordSelection', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124132533_GroupCodesAndTimeAreas') THEN
    ALTER TABLE "TerminalInventories" ADD "Details_TimeAreaCode" character varying(2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124132533_GroupCodesAndTimeAreas') THEN
    ALTER TABLE "TerminalGroups" ADD "Code" character varying(6);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124132533_GroupCodesAndTimeAreas') THEN
    ALTER TABLE "TerminalGroups" ADD "Description" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124132533_GroupCodesAndTimeAreas') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124132533_GroupCodesAndTimeAreas', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124143330_QueuedDownloadSessions') THEN
    ALTER TABLE "DownloadBatches" ADD "NccSessionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124143330_QueuedDownloadSessions') THEN
    CREATE INDEX "IX_DownloadBatches_NccSessionId" ON "DownloadBatches" ("NccSessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124143330_QueuedDownloadSessions') THEN
    CREATE UNIQUE INDEX "IX_DownloadBatches_TerminalId_ActiveTransfer" ON "DownloadBatches" ("TerminalId") WHERE "NccSessionId" IS NOT NULL AND "Status" = 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124143330_QueuedDownloadSessions') THEN
    ALTER TABLE "DownloadBatches" ADD CONSTRAINT "FK_DownloadBatches_NccSessions_NccSessionId" FOREIGN KEY ("NccSessionId") REFERENCES "NccSessions" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124143330_QueuedDownloadSessions') THEN
    UPDATE "DownloadBatches" AS batch
    SET "Status" = 0
    WHERE batch."Status" = 1 AND batch."CompletedAtUtc" IS NULL AND batch."LastError" IS NULL
      AND EXISTS (
        SELECT 1 FROM "AuditEvents" AS audit
        WHERE audit."Category" = 'tables.download' AND audit."Action" IN ('start', 'retry')
          AND audit."Resource" = batch."Id"::text
      )
      AND EXISTS (
        SELECT 1 FROM "DownloadBatchItems" AS item WHERE item."DownloadBatchId" = batch."Id"
      )
      AND NOT EXISTS (
        SELECT 1 FROM "DownloadBatchItems" AS item WHERE item."DownloadBatchId" = batch."Id"
          AND (item."HostDownloadPhase" <> 3 OR item."ItemStatus" <> 1 OR item."Succeeded"
            OR item."LastAckStatus" <> 'pending_terminal_ack')
      );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124143330_QueuedDownloadSessions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124143330_QueuedDownloadSessions', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124152214_NccCaptureMetadata') THEN
    ALTER TABLE "NccSessionFrames" ADD "LinkControl" smallint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124152214_NccCaptureMetadata') THEN
    ALTER TABLE "NccSessionFrames" ADD "SentByHost" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124152214_NccCaptureMetadata') THEN
    ALTER TABLE "NccSessionFrames" ADD "WireLength" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124152214_NccCaptureMetadata') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124152214_NccCaptureMetadata', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124161616_ScreeningSlotDescriptions') THEN
    ALTER TABLE "TableVersions" ADD "ScreeningDescriptionsJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124161616_ScreeningSlotDescriptions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124161616_ScreeningSlotDescriptions', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124163556_ScreeningLayoutProfile') THEN
    ALTER TABLE "TableVersions" ADD "ScreeningLayoutProfile" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124163556_ScreeningLayoutProfile') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124163556_ScreeningLayoutProfile', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124173028_RecoverableRecordUpdates') THEN
    CREATE TABLE "TableRecordUpdateRuns" (
        "Id" uuid NOT NULL,
        "SourceSetId" uuid NOT NULL,
        "RequestJson" text NOT NULL,
        "TargetsJson" text NOT NULL,
        "PreviewToken" text NOT NULL,
        "NextTarget" integer NOT NULL,
        "ChangedCount" integer NOT NULL,
        "SkippedCount" integer NOT NULL,
        "FailureReason" text,
        "EndedAtUtc" timestamp with time zone,
        "Stopped" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TableRecordUpdateRuns" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TableRecordUpdateRuns_TableSets_SourceSetId" FOREIGN KEY ("SourceSetId") REFERENCES "TableSets" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124173028_RecoverableRecordUpdates') THEN
    CREATE INDEX "IX_TableRecordUpdateRuns_SourceSetId_EndedAtUtc" ON "TableRecordUpdateRuns" ("SourceSetId", "EndedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124173028_RecoverableRecordUpdates') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124173028_RecoverableRecordUpdates', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124174821_TableConfigurationIds') THEN
    ALTER TABLE "TableVersions" ADD "ConfigurationId" character varying(6);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124174821_TableConfigurationIds') THEN
    CREATE INDEX "IX_TableVersions_ConfigurationId_TableDefinitionId" ON "TableVersions" ("ConfigurationId", "TableDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124174821_TableConfigurationIds') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124174821_TableConfigurationIds', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124185209_ScheduledDownloadPreparation') THEN
    ALTER TABLE "ScheduledTableDownloads" ADD "CancellationReason" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124185209_ScheduledDownloadPreparation') THEN
    ALTER TABLE "ScheduledTableDownloads" ADD "PreparedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124185209_ScheduledDownloadPreparation') THEN
    ALTER TABLE "DownloadBatches" ADD "ScheduleId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124185209_ScheduledDownloadPreparation') THEN
    UPDATE "DownloadBatches" AS batch
    SET "ScheduleId" = schedule."Id"
    FROM "ScheduledTableDownloads" AS schedule
    WHERE schedule."LastBatchId" = batch."Id";

    UPDATE "ScheduledTableDownloads" AS schedule
    SET "PreparedAtUtc" = batch."CreatedAtUtc"
    FROM "DownloadBatches" AS batch
    WHERE schedule."LastBatchId" = batch."Id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124185209_ScheduledDownloadPreparation') THEN
    CREATE INDEX "IX_ScheduledTableDownloads_AvailableAtUtc" ON "ScheduledTableDownloads" ("AvailableAtUtc") WHERE "LastBatchId" IS NULL AND "NccSessionId" IS NULL AND "CompletedAtUtc" IS NULL AND "CancelledAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124185209_ScheduledDownloadPreparation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124185209_ScheduledDownloadPreparation', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124214411_FirmwareAllocationWindows') THEN
    ALTER TABLE "ScheduledTableDownloads" ADD "FirmwareSlotEndAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124214411_FirmwareAllocationWindows') THEN
    ALTER TABLE "ScheduledTableDownloads" ADD "SourceTableSetId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124214411_FirmwareAllocationWindows') THEN
    CREATE INDEX "IX_ScheduledTableDownloads_SourceTableSetId" ON "ScheduledTableDownloads" ("SourceTableSetId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124214411_FirmwareAllocationWindows') THEN
    ALTER TABLE "ScheduledTableDownloads" ADD CONSTRAINT "FK_ScheduledTableDownloads_TableSets_SourceTableSetId" FOREIGN KEY ("SourceTableSetId") REFERENCES "TableSets" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124214411_FirmwareAllocationWindows') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124214411_FirmwareAllocationWindows', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124222112_OperatorSessionGeneration') THEN
    ALTER TABLE "OperatorAccounts" ADD "SessionGeneration" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124222112_OperatorSessionGeneration') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124222112_OperatorSessionGeneration', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124223745_OperatorPermissionProfiles') THEN
    ALTER TABLE "OperatorAccounts" ADD "PermissionProfileJson" jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124223745_OperatorPermissionProfiles') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124223745_OperatorPermissionProfiles', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124231547_TerminalInmateConfigurations') THEN
    CREATE TABLE "TerminalInmateConfigurations" (
        "Id" uuid NOT NULL,
        "TerminalId" uuid NOT NULL,
        "FeatureConfigurationId" uuid,
        "SetupConfigurationId" character varying(6),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalInmateConfigurations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TerminalInmateConfigurations_InmateFeatureConfigurations_Fe~" FOREIGN KEY ("FeatureConfigurationId") REFERENCES "InmateFeatureConfigurations" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TerminalInmateConfigurations_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124231547_TerminalInmateConfigurations') THEN
    CREATE INDEX "IX_TerminalInmateConfigurations_FeatureConfigurationId" ON "TerminalInmateConfigurations" ("FeatureConfigurationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124231547_TerminalInmateConfigurations') THEN
    CREATE UNIQUE INDEX "IX_TerminalInmateConfigurations_TerminalId" ON "TerminalInmateConfigurations" ("TerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124231547_TerminalInmateConfigurations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124231547_TerminalInmateConfigurations', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124232942_TerminalLifecycleDates') THEN
    ALTER TABLE "Terminals" ADD "DeactivatedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124232942_TerminalLifecycleDates') THEN
    ALTER TABLE "Terminals" ADD "DeletionReviewDate" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124232942_TerminalLifecycleDates') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124232942_TerminalLifecycleDates', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125033139_AddCarrierLibrary') THEN
    CREATE TABLE "CarrierLibraryEntries" (
        "Id" uuid NOT NULL,
        "Code" character varying(6) NOT NULL,
        "CodeKey" character varying(6) NOT NULL,
        "Description" character varying(80) NOT NULL,
        "TableNumber" integer NOT NULL,
        "RowPayload" bytea NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CarrierLibraryEntries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125033139_AddCarrierLibrary') THEN
    CREATE UNIQUE INDEX "IX_CarrierLibraryEntries_TableNumber_CodeKey" ON "CarrierLibraryEntries" ("TableNumber", "CodeKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125033139_AddCarrierLibrary') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125033139_AddCarrierLibrary', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125094627_AddCardMasterDefinitions') THEN
    ALTER TABLE "TableVersions" ADD "CardMasterLinksJson" jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125094627_AddCardMasterDefinitions') THEN
    CREATE TABLE "CardMasterDefinitions" (
        "Id" uuid NOT NULL,
        "Code" character varying(4) NOT NULL,
        "CodeKey" character varying(4) NOT NULL,
        "Name" character varying(80) NOT NULL,
        "TableNumber" integer NOT NULL,
        "RowPayload" bytea NOT NULL,
        "Archived" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_CardMasterDefinitions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125094627_AddCardMasterDefinitions') THEN
    CREATE UNIQUE INDEX "IX_CardMasterDefinitions_TableNumber_CodeKey" ON "CardMasterDefinitions" ("TableNumber", "CodeKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125094627_AddCardMasterDefinitions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125094627_AddCardMasterDefinitions', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125101718_AddQuickAccessCustomers') THEN
    CREATE TABLE "QuickAccessCustomers" (
        "Id" uuid NOT NULL,
        "Reference" character varying(6) NOT NULL,
        "Name" character varying(160) NOT NULL,
        "BillingComments" character varying(2000) NOT NULL,
        "ContactName" character varying(40) NOT NULL,
        "Phone" character varying(64) NOT NULL,
        "Street" character varying(256) NOT NULL,
        "City" character varying(128) NOT NULL,
        "Province" character varying(128) NOT NULL,
        "PostalCode" character varying(32) NOT NULL,
        "Notes" character varying(2000) NOT NULL,
        "Archived" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_QuickAccessCustomers" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_QuickAccessCustomers_Reference" CHECK ("Reference" ~ '^[0-9]{6}$' AND "Reference" <> '000000')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125101718_AddQuickAccessCustomers') THEN
    CREATE UNIQUE INDEX "IX_QuickAccessCustomers_Reference" ON "QuickAccessCustomers" ("Reference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125101718_AddQuickAccessCustomers') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125101718_AddQuickAccessCustomers', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125201100_AddTerminalIdentityCutovers') THEN
    CREATE TABLE "TerminalIdentityCutoverItems" (
        "Id" uuid NOT NULL,
        "ActivatedAtUtc" timestamp with time zone,
        "ActivationError" text,
        "ActivationSessionId" uuid,
        "ApplicationAcknowledgedAtUtc" timestamp with time zone,
        "CancelledAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "DispatchBatchId" uuid,
        "DispatchSessionId" uuid,
        "DispatchStartedAtUtc" timestamp with time zone,
        "DueAtUtc" timestamp with time zone NOT NULL,
        "FinishedAtUtc" timestamp with time zone,
        "JobId" uuid NOT NULL,
        "NewIdentityObservedAtUtc" timestamp with time zone,
        "NewIdentityObservedSessionId" uuid,
        "PossiblyAppliedAtUtc" timestamp with time zone,
        "SourceAccessRecordId" uuid NOT NULL,
        "SourceAccessRecordRevision" integer NOT NULL,
        "SourceAssignmentId" uuid NOT NULL,
        "SourceAssignmentRevision" integer NOT NULL,
        "SourceFirmwareVersionId" uuid NOT NULL,
        "SourcePayloadSha256Hex" character varying(64) NOT NULL,
        "SourceTableSetId" uuid NOT NULL,
        "SourceTableSetRevision" integer NOT NULL,
        "SourceTerminalRevision" integer NOT NULL,
        "SourceWireId" character varying(10) NOT NULL,
        "StagedTableSetId" uuid,
        "TargetPayloadSha256Hex" character varying(64) NOT NULL,
        "TargetWireId" character varying(10) NOT NULL,
        "TerminalId" uuid NOT NULL,
        "TransferEndedAtUtc" timestamp with time zone,
        "TransferError" text,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_TerminalIdentityCutoverItems" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_TerminalIdentityCutoverItems_DecimalIds" CHECK ("SourceWireId" ~ '^[0-9]{10}$' AND "TargetWireId" ~ '^[0-9]{10}$' AND "SourceWireId" <> "TargetWireId"),
        CONSTRAINT "FK_TerminalIdentityCutoverItems_Terminals_TerminalId" FOREIGN KEY ("TerminalId") REFERENCES "Terminals" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125201100_AddTerminalIdentityCutovers') THEN
    CREATE INDEX "IX_TerminalIdentityCutoverItems_DueAtUtc" ON "TerminalIdentityCutoverItems" ("DueAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125201100_AddTerminalIdentityCutovers') THEN
    CREATE INDEX "IX_TerminalIdentityCutoverItems_JobId" ON "TerminalIdentityCutoverItems" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125201100_AddTerminalIdentityCutovers') THEN
    CREATE UNIQUE INDEX "IX_TerminalIdentityCutoverItems_TargetWireId" ON "TerminalIdentityCutoverItems" ("TargetWireId") WHERE "FinishedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125201100_AddTerminalIdentityCutovers') THEN
    CREATE UNIQUE INDEX "IX_TerminalIdentityCutoverItems_TerminalId" ON "TerminalIdentityCutoverItems" ("TerminalId") WHERE "FinishedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125201100_AddTerminalIdentityCutovers') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125201100_AddTerminalIdentityCutovers', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125210814_AddFieldEventCodeSettings') THEN
    CREATE TABLE "FieldEventCodeSettings" (
        "Id" uuid NOT NULL,
        "Kind" character varying(16) NOT NULL,
        "Code" integer NOT NULL,
        "Description" character varying(160),
        "Severity" integer,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FieldEventCodeSettings" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_FieldEventCodeSettings_KindCodeSeverity" CHECK (("Kind" = 'alarm' AND "Code" BETWEEN 0 AND 255 AND ("Severity" IS NULL OR "Severity" BETWEEN 1 AND 4)) OR ("Kind" = 'maintenance' AND "Code" BETWEEN 0 AND 65535 AND "Severity" IS NULL))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125210814_AddFieldEventCodeSettings') THEN
    CREATE UNIQUE INDEX "IX_FieldEventCodeSettings_Kind_Code" ON "FieldEventCodeSettings" ("Kind", "Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125210814_AddFieldEventCodeSettings') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125210814_AddFieldEventCodeSettings', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125211512_AddFieldEventDestinations') THEN
    CREATE TABLE "FieldEventDestinationRules" (
        "Id" uuid NOT NULL,
        "Kind" character varying(16) NOT NULL,
        "Code" integer,
        "RegionCode" character varying(32),
        "GroupId" uuid,
        "TerminalId" uuid,
        "ScopeKey" character varying(160) NOT NULL,
        "Destination" character varying(80) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FieldEventDestinationRules" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_FieldEventDestinationRules_KindCode" CHECK (("Kind" = 'alarm' AND ("Code" IS NULL OR "Code" BETWEEN 0 AND 255)) OR ("Kind" = 'maintenance' AND ("Code" IS NULL OR "Code" BETWEEN 0 AND 65535)))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125211512_AddFieldEventDestinations') THEN
    CREATE TABLE "FieldEventRouteDecisions" (
        "Id" uuid NOT NULL,
        "TransactionId" uuid NOT NULL,
        "Kind" character varying(16) NOT NULL,
        "Code" integer NOT NULL,
        "Status" character varying(16) NOT NULL,
        "Destination" character varying(80),
        "RuleIds" uuid[] NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" text NOT NULL,
        "UpdatedBy" text NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_FieldEventRouteDecisions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FieldEventRouteDecisions_DlogTransactions_TransactionId" FOREIGN KEY ("TransactionId") REFERENCES "DlogTransactions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125211512_AddFieldEventDestinations') THEN
    CREATE UNIQUE INDEX "IX_FieldEventDestinationRules_ScopeKey" ON "FieldEventDestinationRules" ("ScopeKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125211512_AddFieldEventDestinations') THEN
    CREATE UNIQUE INDEX "IX_FieldEventRouteDecisions_TransactionId" ON "FieldEventRouteDecisions" ("TransactionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125211512_AddFieldEventDestinations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125211512_AddFieldEventDestinations', '9.0.1');
    END IF;
END $EF$;
COMMIT;
