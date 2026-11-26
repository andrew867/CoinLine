# Latest reported terminal status

The historical Manager status query combines the terminal's reported serial, status bits and last status receipt time. CoinLine now supplements the event stream with a grouped status snapshot for the selected phone. It reads the latest valid 11-byte MT10 from a matched terminal-to-host NCC session, not derived inventory values or an alarm history. Newer unreadable reports are shown independently; stale valid flags are not presented as newer data. Known clear bits mean not indicated in that report, not verified hardware health. Unmapped set bits are counted explicitly and are not given invented meanings.

The status-only permission supplies just the phone selection, report summary and decoded flags. Raw payloads and full phone inventory remain behind their separate permissions. The existing DLOG decoder uses the same typed status-bit map, with no new wire format.

| Workflow | Editor/console | Decoding | Live terminal evidence | Behavior |
| --- | --- | --- | --- | --- |
| Current terminal status query | Phone selection, reported serial and UTC receipt time, grouped named flags, unmapped bits, retained source link with permissions | Existing MT10 status map shared by DLOG details and the status query | A real loopback NCC attention bundle supplied an MT10 and refreshed the previous valid report. PostgreSQL distinguished matched inbound reports from imports, host-originated records, other phones and malformed records | Physical terminal condition changes remain separate bench checks |

## Acceptance evidence

- 16 focused protocol tests passed, including shared MT10 serial decoding, named asserted and clear flags, unmapped bit counts, invalid serial and malformed records. The historical event summary also identifies unknown bits.
- Five focused backend integration tests passed with isolated PostgreSQL, including status-only access and rejection of unrelated registry and message-log requests. The status fixture verified exact historical receipt times, a newer unreadable body, cross-phone and outbound exclusion, absent reports, and a subsequent authentic loopback NCC report with no asserted flags.
- Eight focused Vitest tests and the production web build passed. The deterministic Chromium workflow selected a phone, displayed its earlier flags and unknown newer report, and refreshed to a valid report with no named conditions while retaining the historical alarm. Failure traces are retained. Stock-hardware status changes have not been tested in this change.
