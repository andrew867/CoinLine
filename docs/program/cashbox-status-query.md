# Cashbox status query parity

The legacy cashbox query is a read-only Manager operation, not a table editor or a collection command. CoinLine provides an independent Cashbox Read screen and a phone-specific shortcut from Terminal status.

| Capability | Manager UI and API | Encoding and evidence | Stock terminal behavior |
| --- | --- | --- | --- |
| Find phone and browse previous or next | Implemented, paged and sorted by NCC wire ID | Not a wire operation | Not applicable |
| Last reported balance, capacity, box condition, box ID and slot counts | Implemented, including unknown flags and distinct manager/phone times | MT38 fixture and matched NCC input tested against PostgreSQL | Physical cashbox run pending |
| Previous box and collection | Implemented, distinct reported totals and removed-box values | MT51 fixture; existing quarter-call accounting and retry regression | Physical collection pending |
| Open query from selected phone | Implemented, restricted to selected record until explicitly browsing all | Not a wire operation | Not applicable |
| Denomination labels specific to terminal profile | Raw slot counts plus positive configured values from a verified receipt-time MT50 reference; unknown or uncertain mappings retain raw slots. | The shared 105-byte MT50 reader matches the native editor's sixteen signed value fields and flags. PostgreSQL fixtures distinguish normalized and inline records, receipt order, wrong-phone sessions, unacknowledged transfers, invalid and ambiguous evidence. The three profile-specific NCC fixtures edit and acknowledge MT50, send a subsequent MT38 and resolve the exact delivered revision without recalculating reported money. | Physical verification and attribution of accumulated coins across configuration changes remain pending. |

Cashbox status is not an estimate from rate quotes, deposited coins or call records. Replacement box status is unknown until a matched report is received. No MT14 acknowledgement or table behavior is claimed for this read-only operation. The existing cashbox restoration path uses only the eligible current report.

In a native Multi-pay emulator trial, two coin call details were received and retained, but neither MT38 status nor MT51 collection was observed. The later successful upload retry contained only status and software-version metadata plus the end-of-data marker. The desktop help specifically describes cashbox status accompanying CDRs for MTR 2.0 graphical terminals; this behavior remains unverified for the earlier registered family used in the trial. A CDR's deposit and charge fields do not establish the current box balance. Verify the terminal's normal status or collection reporting on each relevant family without manufacturing a cashbox record.
