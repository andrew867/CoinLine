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

## Retained-state restoration

A subsequent isolated native run restarted from the normally saved restricted state. Before the selected refresh, the ordinary paid control still displayed `No access to that number`, and the free control still displayed `Calling...`. The disabled record retained the same daily window and reserved bytes; only the restriction level changed to zero.

| Evidence | Result |
| --- | --- |
| Encoded disabled record | 26 bytes; SHA-256 `111928d2a0b35b01dac4f193ffd81b9e468d0f8bf0cc7d1590b667fecf403c9a` |
| Terminal acknowledgement | Original selected batch completed, item application result ACK, one terminal-originated clear, session closed |
| Panel | `Completed 02` following ordinary Craft refresh |
| Restored paid local control | `Please insert card or deposit $0.25`; no payment input and no destination outpulse |
| Free local control | `Calling...` before and after restoration; two complete local destination outpulses across the run |
| Closing state | Returned to the ordinary idle screen and exited normally |

The opt-in test runner encountered a file-sharing error while replacing its progress snapshot. The staged snapshot already recorded the completed acknowledgement and terminal clear. A separate read-only query of the retained PostgreSQL database independently confirmed the exact disabled record digest, completed batch, successful application ACK, closed session and one terminal clear. The evidence writer now retries brief sharing failures with a fixed bound. The native run is not described as a successful xUnit execution; the independent database and panel observations establish the restoration behavior.

Timed expiry, overnight transitions, cover override, levels 2 through 4, other terminal profiles and physical acceptance remain unqualified.

## Repeating the host bench

The opt-in `NativeServiceRestrictionBenchTests` integration test requires a fresh isolated PostgreSQL database, a new empty evidence directory supplied as `COINLINE_NATIVE_RESTRICTION_RUN_DIR`, and a fictional independent wire identity supplied as `COINLINE_NATIVE_RESTRICTION_WIRE_ID`. The test creates a loopback listener on an available port and writes `ready-enabled.json` with the selected batch and digest. After observing the first acknowledgement, the operator can release the separate disabled batch by placing `disable.ready` in the evidence directory. Set `COINLINE_NATIVE_RESTRICTION_PHASE` to `enabled` or `disabled` for a single-stage bench; the default is `both`. A `stop.ready` file terminates a waiting bench without claiming the unfinished stage. The test does not start an emulator, modify the shared Manager, send media or call an external service. It is skipped in normal CI.
