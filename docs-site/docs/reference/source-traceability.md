# Implementation traceability (engineering)

!!! warning "Internal engineering reference"
    This matrix maps features to code paths for **engineering support**. It is **not** required reading for operators. Customer-facing validation tracking is **[Compatibility validation items](compatibility-validation-items.md)**.

| Feature area | Backend | Console routes | Primary persistence | Tests |
|--------------|---------|----------------|---------------------|-------|
| NCC framing | `HostPlatform.Protocols.Ncc` | Related diagnostics pages | `NccFrameCapture`, `NccSession` | Golden, protocol |
| DLOG ingest | `DlogTransactionEngine`, `DlogController` | `/dlog` | `DlogTransaction` | Integration, protocol |
| Tables | `TableDistributionService` | Table + download pages | `TableVersion`, `DownloadBatch` | Unit, integration |
| Rating | `RatingEngine` | Rating tools | `RatePlan*`, `CallRecord` | Unit |
| Cards | `CardsController` | Card admin | Card\* entities | Integration |
| Craft | `CraftController` | `/craft` | `CraftSession` | Integration |
| Firmware packages | `FirmwareJobOrchestrator` | `/firmware/*` | `FirmwareUpdateJob` | Integration |
| Hardware validation | `HardwareValidationController` | — | `CapturedHardwareSession` | Integration |
