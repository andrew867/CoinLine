START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardWriteEvents" DROP COLUMN "IntendedOperation";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardWriteEvents" DROP COLUMN "RawPayloadJson";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardReadEvents" DROP COLUMN "RawPayloadJson";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardWriteEvents" ADD "PanLast4" character varying(4);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardWriteEvents" ADD "ReportedCardType" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardReadEvents" ADD "PanLast4" character varying(4);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardReadEvents" ADD "Result" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    UPDATE "CardReadEvents" AS event SET "PanLast4" = account."PanLast4"
      FROM "CardAccounts" AS account WHERE event."CardAccountId" = account."Id"
        AND account."PanLast4" ~ '^[0-9]{4}$';
    UPDATE "CardWriteEvents" AS event SET "PanLast4" = account."PanLast4",
      "ReportedCardType" = CASE WHEN account."ResolvedCardType" BETWEEN 0 AND 6 THEN account."ResolvedCardType" ELSE 0 END
      FROM "CardAccounts" AS account WHERE event."CardAccountId" = account."Id"
        AND account."PanLast4" ~ '^[0-9]{4}$';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardWriteEvents" ADD CONSTRAINT "CK_CardWriteEvents_PanLast4" CHECK ("PanLast4" IS NULL OR "PanLast4" ~ '^[0-9]{4}$');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    ALTER TABLE "CardReadEvents" ADD CONSTRAINT "CK_CardReadEvents_PanLast4" CHECK ("PanLast4" IS NULL OR "PanLast4" ~ '^[0-9]{4}$');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125223508_MinimizeCardEventData') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261125223508_MinimizeCardEventData', '9.0.1');
    END IF;
END $EF$;
COMMIT;
