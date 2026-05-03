# Protocol compatibility overview

**CoinLine Server** implements protocol interfaces aligned with **supported Millennium payphone models** and **OEM-supported integration** profiles. Compatibility is expressed through:

- **Framing and diagnostics** for host–terminal exchanges (NCC-oriented framing paths).
- **DLOG-style transaction ingestion** with **raw payload preservation** and structured diagnostics when decode paths are uncertain.
- **Table distribution** with checksum-oriented payloads.
- **Firmware package management** workflows where enabled (metadata, jobs, simulation vs gated live execution).

## Customer-facing stance

- Behavior is validated against **published compatibility materials**, **certified terminal firmware versions**, and **customer field validation** programs — not against informal internal artifacts.
- Unknown message types or ambiguous decode outcomes are retained for diagnosis and may require **device validation in the customer lab**.

## Where to go next

| Topic | Doc |
|-------|-----|
| NCC framing | [NCC framing](ncc-framing.md) |
| DLOG ingestion | [DLOG](dlog.md) |
| Tables | [Table distribution](table-distribution.md) |
| Field evidence | [Field validation](../field-validation/overview.md) |
