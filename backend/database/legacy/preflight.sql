-- Existing databases only. Refuse unknown, empty or non-contiguous migration histories.
DO $coinline$
DECLARE
    known text[] := ARRAY[
        '20260502182836_InitialCreate',
        '20260502190509_AddNccFrameCaptures',
        '20260502194017_AddDlogTranche3',
        '20260502200409_AddTableDistributionTranche4',
        '20260502201146_TerminalTableAssignmentUnique',
        '20260502204325_Tranche5RatingAndCallRecords',
        '20260502212615_Tranche6CardPaymentsSkeleton',
        '20260502214857_Tranche7CraftFieldOperations',
        '20260502222110_Tranche8FirmwareRegistry',
        '20260502234611_Tranche10ProductionReadiness',
        '20260503010027_CapturedHardwareSessions',
        '20260503034129_HostDownloadPhaseColumn',
        '20260503072310_NccSessionLifecycleStatus',
        '20261123130622_NccSessionFramesAndCallPayloads',
        '20261123140456_OperatorAccounts',
        '20261124045139_TerminalGroupMemberships',
        '20261124050650_ScheduledTableDownloads',
        '20261124052644_FirmwareTableTargets',
        '20261124095532_TerminalFamilyInventory',
        '20261124101318_InmateFeatureConfigurations',
        '20261124120729_TerminalCallbackRequests',
        '20261124123642_TerminalInventories',
        '20261124130248_ScheduledRecordSelection',
        '20261124132533_GroupCodesAndTimeAreas',
        '20261124143330_QueuedDownloadSessions',
        '20261124152214_NccCaptureMetadata',
        '20261124161616_ScreeningSlotDescriptions',
        '20261124163556_ScreeningLayoutProfile',
        '20261124173028_RecoverableRecordUpdates',
        '20261124174821_TableConfigurationIds',
        '20261124185209_ScheduledDownloadPreparation',
        '20261124214411_FirmwareAllocationWindows',
        '20261124222112_OperatorSessionGeneration',
        '20261124223745_OperatorPermissionProfiles',
        '20261124231547_TerminalInmateConfigurations',
        '20261124232942_TerminalLifecycleDates',
        '20261125033139_AddCarrierLibrary',
        '20261125094627_AddCardMasterDefinitions',
        '20261125101718_AddQuickAccessCustomers',
        '20261125201100_AddTerminalIdentityCutovers',
        '20261125210814_AddFieldEventCodeSettings',
        '20261125211512_AddFieldEventDestinations',
        '20261125223508_MinimizeCardEventData'
    ];
    observed text[];
    baseline text := '20261126002309_CoinLineBaseline';
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory')) IS NULL THEN
        RAISE EXCEPTION 'No CoinLine migration history in the selected schema. Use the normal baseline for a new database.';
    END IF;
    SELECT array_agg("MigrationId"::text ORDER BY "MigrationId") INTO observed FROM "__EFMigrationsHistory";
    IF observed = known || ARRAY[baseline] OR observed = known || ARRAY[baseline, '20261126132338_AddCallPricingStructures']
       OR observed = known || ARRAY[baseline, '20261126132338_AddCallPricingStructures', '20261126142250_AddCraftTechnicianAttribution']
       OR observed = known || ARRAY[baseline, '20261126132338_AddCallPricingStructures', '20261126142250_AddCraftTechnicianAttribution', '20261127030044_AddNccVersionObservation'] THEN
        RETURN;
    END IF;
    IF observed IS NULL OR cardinality(observed) > cardinality(known)
       OR observed <> known[1:cardinality(observed)] THEN
        RAISE EXCEPTION 'Unexpected CoinLine migration history. Stop and review the database before upgrading.';
    END IF;
END $coinline$;
