# Route catalog (audit)

Every implemented UI route (`coinline/web/src/App.tsx`) with operator-facing purpose and primary API dependencies.

!!! tip "Developer vs operator"
    Operators follow task docs in [Operator guide](../operator-guide/overview.md). Developers extend routes here and matching API clients.

| Route | Page component | Primary APIs | Notes |
|-------|-----------------|--------------|-------|
| `/` | `Dashboard` | `/api/operator/dashboard`, search | Aggregates — **not** modem truth |
| `/customers` | `CustomersPage` | `/api/customers` | |
| `/customers/:id` | `CustomerDetail` | `/api/customers/{id}`, operator console/timeline | |
| `/sites` | `SitesPage` | `/api/sites` | |
| `/terminals` | `TerminalsList` | `/api/terminals` | |
| `/terminals/new` | `TerminalCreatePage` | `POST /api/terminals` | |
| `/terminals/:id` | `TerminalDetail` | `/api/terminals/{id}`, events, downloads, firmware | |
| `/ncc-sessions` | JsonPanel | `/api/ncc/sessions` | Read-only list |
| `/ncc-frame-captures` | `NccFrameCaptures` | `/api/ncc/frame-captures` | Upload captures — sensitive |
| `/dlog` | `DlogTransactions` | `/api/dlog/transactions` | |
| `/dlog/:id` | `DlogTransactionDetail` | `/api/dlog/transactions/{id}` | Hex viewer |
| `/dlog/replay-debug` | `DlogReplayDebug` | `/api/dlog/replay` | **Dangerous** export — confirm |
| `/table-definitions` | `TableDefinitions` | `/api/tables/definitions` | |
| `/table-definitions/:id` | `TableDefinitionDetail` | definitions API | |
| `/table-versions` | `TableVersions` | `/api/tables/versions` | |
| `/table-sets` | `TableSets` | `/api/tables/sets` | |
| `/table-sets/:id` | `TableSetDetail` | `/api/tables/sets/{id}`, publish | Confirm publish |
| `/downloads` | `DownloadsPage` | `/api/downloads` | |
| `/downloads/:id` | `DownloadDetail` | `/api/downloads/{id}` | Cancel/retry confirm |
| `/uploads` | `UploadsPage` | `/api/uploads` | |
| `/uploads/:id` | `UploadDetail` | `/api/uploads/{id}`, ingest / reprocess / review | |
| `/rate-plans` | `RatePlansPage` | `/api/rate-plans` | |
| `/rate-plans/:id` | `RatePlanDetail` | `/api/rate-plans/{id}`, versions | Publish confirm |
| `/number-classes` | `NumberClassesPage` | `/api/number-classes` | Blocked class confirm |
| `/rating-quote` | `RatingQuoteTool` | `POST /api/rating/quote` | MVP — **not** full tariffs |
| `/call-records` | `CallRecordsPage` | `/api/call-records` | |
| `/call-records/:id` | `CallRecordDetail` | `/api/call-records/{id}`, reconcile | Reconcile confirm |
| `/card-products` | `CardProductsPage` | `/api/cards/products` | |
| `/card-products/:id` | `CardProductDetail` | `/api/cards/products/{id}` | |
| `/card-accounts` | `CardAccountsPage` | `/api/cards/accounts` | |
| `/card-accounts/:id` | `CardAccountDetail` | `/api/cards/accounts/{id}` | Simulation banner |
| `/payment-transactions` | `PaymentTransactionsPage` | `/api/cards/transactions` | |
| `/smartcard-types` | `SmartcardTypesPage` | `/api/smartcards/types` | Catalog |
| `/card-reconciliation` | `ReconciliationBatchesPage` | `/api/cards/reconciliation-batches` | |
| `/card-reconciliation/:id` | `ReconciliationBatchDetail` | batch APIs | Post/close/exception confirm |
| `/craft` | `CraftSessionsPage` | `/api/craft/sessions` | **SIMULATION ONLY** execution |
| `/craft/:sessionId` | `CraftSessionDetail` | `/api/craft/sessions/{id}` | |
| `/craft/commands/:commandId` | `CraftCommandDetail` | `/api/craft/commands/{id}` | |
| `/firmware/packages` | `FirmwarePackagesPage` | `/api/firmware/packages` | |
| `/firmware/packages/:id` | `FirmwarePackageDetail` | package + artifacts | |
| `/firmware/jobs` | `FirmwareJobsPage` | `/api/firmware/jobs` | |
| `/firmware/jobs/:id` | `FirmwareJobDetail` | `/api/firmware/jobs/{id}` | Live flash banner |
| `/firmware/versions` | `FirmwareVersionsPage` | `/api/firmware/versions` | |
| `/firmware/targets` | `FirmwareTargetsPage` | `/api/firmware/targets` | |
| `/audit` | `AuditEventsPage` | `/api/audit/events` | |
| `/status` | `StatusPage` | `/health`, `/ready`, `/metrics` | Readiness UI |
| `/settings` | inline | — | Static proxy/API key note |
| `*` | `NotFoundPage` | — | |
