# Rating & call flow

`RatingEngine` evaluates the host plan for HTTP quotes and real-time firmware calls. When a terminal sends a type 63 request, the listener reads the packed BCD digits and resolves the terminal's published rate plan. It writes a type 64 response and stores the request, response, decision, and audit event. The first period and charge are encoded in seconds and cents.

A standard 25-byte MT63 request may carry up to two complete call-detail records in its immediate-clear packet. The listener validates and commits those separately, appends their MT5 receipts to the MT64 response and keeps the protected suffix out of the persisted quote and frame captures. A same-link retransmission replays the complete response instead of creating a second quote. This dialogue does not pick up a scheduled or assigned configuration.

| Type 64 status | Result |
|----------------|--------|
| `1` | Local rate found |
| `3` | Rate unavailable |
| `4` | Number blocked |
| `5` | Intra-area toll rate found |
| `6` | Inter-area toll rate found |

The local lab verified a paid quote and a correctly displayed unavailable rate. The firmware can also screen a toll-free route locally and proceed to the call without requesting a host rate. Rated calls and terminal table behavior need validation for each deployed firmware and plan. See [Call records API](../api/endpoints/call-records.md).
