# Entity reference (complete DbSet list)

Mapped in **`HostPlatformDbContext`** — authoritative names match EF model.

| DbSet | CLR entity |
|-------|------------|
| `Customers` | `Customer` |
| `Sites` | `Site` |
| `TerminalGroups` | `TerminalGroup` |
| `FirmwareVersions` | `FirmwareVersion` |
| `TransportEndpoints` | `TransportEndpoint` |
| `Terminals` | `Terminal` |
| `TerminalEvents` | `TerminalEvent` |
| `TerminalStatusRecords` | `TerminalStatusRecord` |
| `NccSessions` | `NccSession` |
| `NccFrameCaptures` | `NccFrameCapture` |
| `CapturedHardwareSessions` | `CapturedHardwareSession` |
| `DlogTransactions` | `DlogTransaction` |
| `DlogMessageTypes` | `DlogMessageType` |
| `DlogParseDiagnostics` | `DlogParseDiagnostic` |
| `DlogCorrelationLinks` | `DlogCorrelationLink` |
| `DlogReplayRequests` | `DlogReplayRequest` |
| `UploadBatches` | `UploadBatch` |
| `UploadRecords` | `UploadRecord` |
| `TableDefinitions` | `TableDefinition` |
| `TablePayloads` | `TablePayload` |
| `TableSets` | `TableSet` |
| `TableVersions` | `TableVersion` |
| `CustomerTableOverrides` | `CustomerTableOverride` |
| `SiteTableOverrides` | `SiteTableOverride` |
| `TerminalTableOverrides` | `TerminalTableOverride` |
| `TerminalTableAssignments` | `TerminalTableAssignment` |
| `DownloadBatches` | `DownloadBatch` |
| `DownloadBatchItems` | `DownloadBatchItem` |
| `RatePlans` | `RatePlan` |
| `RatePlanVersions` | `RatePlanVersion` |
| `RateRules` | `RateRule` |
| `DestinationPrefixes` | `DestinationPrefix` |
| `TimeBands` | `TimeBand` |
| `Tariffs` | `Tariff` |
| `DialedNumberClasses` | `DialedNumberClass` |
| `CallAuthorizationRequests` | `CallAuthorizationRequest` |
| `CallRecords` | `CallRecord` |
| `RatingResults` | `RatingResult` |
| `RatingDiagnostics` | `RatingDiagnostic` |
| `CallChargeSegments` | `CallChargeSegment` |
| `CardProducts` | `CardProduct` |
| `CardAccounts` | `CardAccount` |
| `SmartcardTypes` | `SmartcardType` |
| `EPurseAccounts` | `EPurseAccount` |
| `PaymentTransactions` | `PaymentTransaction` |
| `BalanceAdjustments` | `BalanceAdjustment` |
| `CardBalances` | `CardBalance` |
| `CardCredentials` | `CardCredential` |
| `CardReadEvents` | `CardReadEvent` |
| `CardWriteEvents` | `CardWriteEvent` |
| `CardReconciliationBatches` | `CardReconciliationBatch` |
| `SmartcardProfiles` | `SmartcardProfile` |
| `EPurseProfiles` | `EPurseProfile` |
| `CraftSessions` | `CraftSession` |
| `CraftCommands` | `CraftCommand` |
| `CraftAuditEvents` | `CraftAuditEvent` |
| `CraftCommandTypes` | `CraftCommandType` |
| `CraftDiagnostics` | `CraftDiagnostic` |
| `TerminalDiagnosticSnapshots` | `TerminalDiagnosticSnapshot` |
| `RemoteTableReloadRequests` | `RemoteTableReloadRequest` |
| `CdrUploadRequests` | `CdrUploadRequest` |
| `FirmwarePackages` | `FirmwarePackage` |
| `FirmwareArtifacts` | `FirmwareArtifact` |
| `FirmwareCompatibilityRules` | `FirmwareCompatibilityRule` |
| `FirmwareBlockManifests` | `FirmwareBlockManifest` |
| `FirmwareTargets` | `FirmwareTarget` |
| `FirmwareUpdateJobs` | `FirmwareUpdateJob` |
| `FirmwareRollBackPlans` | `FirmwareRollBackPlan` |
| `FirmwareUpdateSafetyChecks` | `FirmwareUpdateSafetyCheck` |
| `FirmwareUpdateSteps` | `FirmwareUpdateStep` |
| `AuditEvents` | `AuditEvent` |
| `SubsystemHeartbeats` | `SubsystemHeartbeat` |

Physical schema: EF migrations under `backend/src/HostPlatform.Infrastructure/Persistence/Migrations/`.
