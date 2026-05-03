# Protocol components (implementation map)

This page lists **CoinLine Server** protocol modules, where tests live, and where to find **fixtures** for integration checks. Wording is **customer-safe** — it does not describe proprietary terminal firmware contents.

| Area | Code / library | Automated tests | Fixture root | Notes |
|------|------------------|-----------------|--------------|--------|
| NCC frame codec | `HostPlatform.Protocols.Ncc` | Golden, protocol | `fixtures/ncc/` | Strict vs diagnostic gap-preserving decode; **field validation** on real lines |
| DLOG pipeline | `HostPlatform.Protocols.Dlog`, `DlogTransactionEngine` | Protocol, integration | `fixtures/dlog/` | Raw payload always stored; diagnostics for uncertain decodes |
| Table distribution | `TableDistributionService` | Unit, integration | `fixtures/tables/` | Opaque payload + checksum; confirm-guarded API |
| Upload ingestion | `UploadsController` + models | Integration | `fixtures/protocol/` | Align captures with [Upload ingestion](upload-ingestion.md) |
| Hardware validation API | `HardwareValidationController` | Integration | `fixtures/hw_validation/` | Evidence capture workflow |

!!! note "Field tools"
    **CoinLine Field Tools** include Management Console pages and API routes used for capture, inspection, and validation — see [Field validation](../field-validation/overview.md).
