# Graphical service restriction qualification

On 2026-11-28 UTC, an independently installed graphical Multi-Pay terminal was tested using a dedicated loopback NCC listener and separate PostgreSQL database. The shared Manager and its terminal assignments were not used. The test used ordinary panel controls, a saved installed state, and reserved local-only destinations, with no payment input or external call.

## Selected record

The native editor encoded a 26-byte MT58 configured for level 1, free and emergency calls only, starting at 00:00 for 24 hours. All 20 reserved bytes were zero. The record was published in the independent bench and queued as a partial download containing only MT58.

| Evidence | Result |
| --- | --- |
| Encoded record | 26 bytes; SHA-256 `d46ef28e3f88ebe6ab57652cd7fc5fcfaa4a107d53843603f98ce765e6e198bf` |
| Terminal acknowledgement | The original one-record batch completed, item application result ACK, terminal-originated clear recorded, session closed |
| Panel | `Completed 02` after the ordinary Craft force-download |
| Baseline ordinary paid local control | Payment prompt for 25 cents; no coin inserted |
| Restricted ordinary paid local control | `No access to that number`; no completed destination outpulse |
| Baseline and restricted free local control | `Calling...` and one complete local destination outpulse in each condition |

The restriction therefore changed the actual terminal's access decision for the tested paid call without blocking the tested free route. No answer or payment completion is claimed for those free controls. This is native emulator evidence, not a physical telephone qualification.

The disabled record was separately prepared with the same daily window and only the level byte changed to zero. The second native run was deferred while the operator was using other terminals; its isolated batch was still pending and was not sent. The enabled run and terminal-clear readback were retained. Restoration, timed expiry, overnight transitions, cover override, levels 2 through 4, other terminal profiles and physical acceptance remain unqualified.

## Repeating the host bench

The opt-in `NativeServiceRestrictionBenchTests` integration test requires a fresh isolated PostgreSQL database, a new empty evidence directory supplied as `COINLINE_NATIVE_RESTRICTION_RUN_DIR`, and a fictional independent wire identity supplied as `COINLINE_NATIVE_RESTRICTION_WIRE_ID`. The test creates a loopback listener on an available port and writes `ready-enabled.json` with the selected batch and digest. After observing the first acknowledgement, the operator can release the separate disabled batch by placing `disable.ready` in the evidence directory. Set `COINLINE_NATIVE_RESTRICTION_PHASE` to `enabled` or `disabled` for a single-stage bench; the default is `both`. A `stop.ready` file terminates a waiting bench without claiming the unfinished stage. The test does not start an emulator, modify the shared Manager, send media or call an external service. It is skipped in normal CI.
