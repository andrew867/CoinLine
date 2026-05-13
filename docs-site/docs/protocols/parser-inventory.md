# Protocol components (implementation map)

This page lists **CoinLine Server** protocol modules, where tests live, and where to find **fixtures** for integration checks. Descriptions cover observable on-wire behavior only — they do not describe proprietary terminal firmware contents.

| Area | Code / library | Automated tests | Fixture root | Notes |
|------|------------------|-----------------|--------------|--------|
| NCC frame codec | `HostPlatform.Protocols.Ncc` | Golden, protocol | `coinline/fixtures/ncc/` | Strict vs diagnostic gap-preserving decode; **field validation** on real lines |
| DLOG pipeline | `HostPlatform.Protocols.Dlog`, `DlogTransactionEngine` | Protocol, integration | `coinline/fixtures/dlog/` | Raw payload always stored; diagnostics for uncertain decodes |
| Table distribution | `TableDistributionService` | Unit, integration | `coinline/fixtures/tables/` | Opaque payload + checksum; confirm-guarded API |
| Upload ingestion | `UploadsController` + models | Integration | `coinline/fixtures/protocol/` | Align captures with [Upload ingestion](upload-ingestion.md) |
| Hardware validation API | `HardwareValidationController` | Integration | `coinline/fixtures/hw_validation/` | Evidence capture workflow |

!!! note "Field tools"
    **CoinLine Field Tools** include Management Console pages and API routes used for capture, inspection, and validation — see [Field validation](../field-validation/overview.md).
