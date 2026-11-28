# Quick Access Key qualification (MT55)

MT55 has ten physical positions. The five-key layout uses positions 6 through 10, not positions 1 through 5. The typed editor and transition review preserve those positions. A selected Clear operation removes the chosen key's destination, references and active controls while retaining its reserved flags.

## Observed acceptance, 2026-11-28

An independent graphical emulator retained a previously installed configuration. The Manager used a separate PostgreSQL database, an isolated loopback NCC listener and a local-only call destination. There was no external telephone call, payment or change to another terminal.

| Check | Result |
| --- | --- |
| Native editor encoding | Both profile layouts generate a 571-byte record. Four boundary-position tests verified exact unchanged saves, no changes outside the selected slot and exact Clear back to the blank template. |
| Configured MT55 | One selected record on the original queued batch, application MT14 received, terminal clear and session closed. Terminal displayed `Completed 02`. |
| Configured key behavior | Physical position 10, from a lifted handset without keypad dialing, card or coin, emitted the exact configured ten-digit destination. The local line answered. |
| Configured key persistence | A separate warm restart from the terminal's own retained state emitted the same destination using the same physical key before receiving the cleared record. |
| Cleared MT55 | A distinct selected batch, application MT14 received, terminal clear and session closed. Terminal displayed `Completed 02`. |
| Cleared key behavior | The same key displayed `The button is blank / Dial or use another`. No outgoing digits during the observation. |
| Clear persistence and ordinary call control | Another warm restart without a Manager connection showed the blank-button prompt again, with no outgoing digits. After an ordinary hook cycle, manual dialing of the same local destination produced the exact digits and local answer. The terminal returned to idle and exited normally. |

The two generated control records had these SHA-256 digests:

| Record | SHA-256 |
| --- | --- |
| Configured | `254f9d168d10a954d0dd6252ee3dcbb9b8830ff3a4b76258d76422d49aa68049` |
| Cleared | `baeb7c8670bdc07776d87632318dd6fb8468358ccab4cc058202b70c5016ad79` |

This test qualifies an edited free Quick Access Key, its Clear operation and persistence on the tested graphical profile. Custom text, paid/carrier overrides, customer reporting, messaging, other positions and physical terminals remain separate checks.

## Earlier five-key profile

The archived operator help explicitly assigns the five-key layout to table positions 6 through 10. A separate earlier Multi-pay emulator with the five-key panel received a configured position 6 record with a synthetic local-only short destination. The selected record received MT14 and the terminal displayed `Completed 02`.

The configured position did not dial through the tested panel. Its sixth input produced no outgoing digits or changed prompt; the five enabled inputs displayed the blank-button prompt and likewise emitted no digits. An ordinary call to the same short destination emitted all four digits and reached the local answer control. This distinguishes a panel-to-record position problem from a failure to receive MT55 or a general rejection of the destination.

The earlier configured control record SHA-256 was `b9ac003e2a33aee6561172d581d73b868dcb430c14d365f5495fd0b82cee8769`. A distinct selected Clear record, with the same blank-template digest as the graphical control above, also received MT14 and a normal terminal clear, and displayed `Completed 02`. The editor layout remains based on the documented positions 6 through 10. Do not renumber its rows to compensate for the unqualified panel behavior. Earlier native positive-key behavior remains open; the two earlier selected acknowledgements do not qualify it.

## Reproduce the isolated Manager side

Use `NativeQuickAccessBenchTests` with a fresh dedicated PostgreSQL database and an empty, private evidence directory. Set:

- `COINLINE_NATIVE_QUICK_ACCESS_RUN_DIR` to that evidence directory.
- `COINLINE_NATIVE_QUICK_ACCESS_WIRE_ID` to an independently isolated fictional terminal identity.
- `COINLINE_NATIVE_QUICK_ACCESS_PROFILE` to `earlier` or `graphical`.
- `COINLINE_NATIVE_QUICK_ACCESS_POSITION` to a supported physical position.
- `COINLINE_NATIVE_QUICK_ACCESS_PHASE` to `both`, `configured` or `cleared`.
- `COINLINE_NCC_LISTEN=1` and the isolated PostgreSQL test connection setting.

Run the focused integration test. Its ready file supplies the ephemeral loopback port, batch ID and payload digest. Connect only the independently selected terminal, perform its ordinary selected download, and observe the acknowledgement and normal close. For the two-phase test, release the `clear.ready` gate after the configured-key observation and a normal exit. Repeat using the terminal's retained state. The bench does not launch the terminal or place calls.

The host and terminal evidence are distinct: the test verifies selected-record acknowledgements; the physical inputs, display and local line observations verify behavior. Keep raw captures and machine-specific settings private. The default CI run checks the generated record cases and skips actual terminal operation without explicit lab configuration.
