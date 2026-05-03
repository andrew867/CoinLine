# Hardware validation harness API

**Route prefix:** `/api/hw-validation`

| Method | Path |
|--------|------|
| GET | `/api/hw-validation/checklists` |
| GET | `/api/hw-validation/evidence-guide` |
| GET | `/api/hw-validation/captured-sessions` |
| GET | `/api/hw-validation/captured-sessions/{id}` |
| POST | `/api/hw-validation/captured-sessions` (**schemaVersion 1**) |
| POST | `/api/hw-validation/captured-sessions/{id}/replay` |

Replay returns **`globalHardwareValidationRequired: true`** — never implies modem certification.

**Docs:** repository `docs/host_platform/hw_validation/`

**Entity:** `CapturedHardwareSession`
