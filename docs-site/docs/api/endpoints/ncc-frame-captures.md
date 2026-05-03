# NCC frame captures API

**Route prefix:** `/api/ncc/frame-captures`

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/ncc/frame-captures` | List captures |
| GET | `/api/ncc/frame-captures/{id}` | Inspect parse stream |
| POST | `/api/ncc/frame-captures` | Upload raw UART `.bin` |
| DELETE | `/api/ncc/frame-captures/{id}` | Delete (**RequireOperator**, `confirm=true`) |

!!! danger "Sensitive binary"
    Raw UART may contain dial strings or credentials — treat exports as confidential.

**UI:** `/ncc-frame-captures`

**Entity:** `NccFrameCapture`
