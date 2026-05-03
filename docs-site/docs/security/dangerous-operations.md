# Dangerous operations (guardrails)

Consolidated index — **always** read endpoint-specific docs for exact parameters.

| Operation | Guardrail | Audit / notes |
|-----------|-----------|---------------|
| DLOG replay export | `POST /api/dlog/replay` requires `?confirm=true` or `confirmExport: true` | Category `dlog` / `replay_export` |
| NCC capture delete | `DELETE .../ncc/frame-captures/{id}?confirm=true` | Operator policy |
| Table publish / assignment / rollback | `confirm=true` on mutating routes | `tables.*` categories |
| Firmware job approve | Admin policy | Firmware audit |
| Firmware live flashing | **`Firmware:AllowLiveFlashing`** default **false** | **PRODUCTION GUARD REQUIRED** |
| Card reconciliation close/exception | JSON `confirm: true` | `cards` category |
| Card physical writes | **`SIMULATION ONLY`** / disabled by default | Returns conflict if not simulation |
| Raw payload retention trim | **`Platform:Retention:AllowDestructiveRawPayloadTrim`** default false | **HARDWARE VALIDATION REQUIRED** sign-off if enabled |
| Captured-session import | Evidence sensitivity — treat as confidential | `hw_validation.captured_session` |

!!! danger "SIMULATION ONLY"
    Craft command execution, card writes, and default firmware job paths do **not** prove field hardware behavior.
