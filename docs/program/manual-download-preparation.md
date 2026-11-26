# Manually selected phone download preparation

The legacy manager offered a selection list for packing the existing configurations of up to forty individual terminals, rather than requiring the operator to choose a single source configuration for all of them. Prepare-now and dated host preparation were distinct from the terminal making contact and acknowledging the tables.

CoinLine's Downloads page supports search by phone ID or name, a removable list of up to forty distinct phones, and immediate or future UTC preparation. A read-only preview shows each phone's own published assignment and record list. All selected phones must be eligible. The confirmation compares a deterministic snapshot of the assignment and record identities with the reviewed selection, and commits the schedules together. It does not change assignments, replace existing pending work or schedule firmware parameters. Immediate preparation creates each batch before phone contact. Future preparation pins the reviewed published set until the scheduled worker packs it.

| Workflow | Editor/console | Host preparation/encoding | Terminal acknowledgement | Behavior |
| --- | --- | --- | --- | --- |
| Manually selected phone list | Search, add/remove, review, explicit preparation time, confirmation and batch links | Reuses validated assigned records and the existing scheduler; no new record format | Two real PostgreSQL-backed loopback NCC sessions delivered their respective full synthetic records byte for byte, and each MT14 was recorded on the originally prepared batch; no third phone was scheduled. | Stock phone and physical bench validation remain separate |

## Verification

- Isolated PostgreSQL integration: seven tests passed across list preparation, table permissions and account access. The list tests verified different assigned sets, exact version identities and record bytes, no write at preview, competing confirmations (one accepted, one conflict), stale assignment and preparation-time rejection, inactive/missing/unassigned phone handling, and future worker preparation of the originally reviewed set after an assignment changed. The two immediate schedules completed over real loopback TCP with application acknowledgements; physical firmware remains untested.
- WebUI: ten focused Vitest tests passed, including four list-preparation tests. The page does not request denied registers or download history for an Add-only operator. Chromium verified the distinct assigned configurations, removal of a blocked phone and confirmation of the exact remaining list; the existing failed-download browser regression also passed. The production web build passed.
