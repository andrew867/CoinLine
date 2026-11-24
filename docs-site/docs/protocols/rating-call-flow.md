# Rating & call flow

`RatingEngine` evaluates the host plan for HTTP quotes and real-time firmware calls. When a terminal sends a type 63 request, the listener reads the packed BCD digits and resolves the terminal's published rate plan. It writes a type 64 response and stores the request, response, decision, and audit event. The first period and charge are encoded in seconds and cents.

| Type 64 status | Result |
|----------------|--------|
| `1` | Local rate found |
| `3` | Rate unavailable |
| `4` | Number blocked |
| `5` | Intra-area toll rate found |
| `6` | Inter-area toll rate found |

The local lab verified a paid quote and a correctly displayed unavailable rate. The firmware can also screen a toll-free route locally and proceed to the call without requesting a host rate. Rated calls and terminal table behavior need validation for each deployed firmware and plan. See [Call records API](../api/endpoints/call-records.md).
