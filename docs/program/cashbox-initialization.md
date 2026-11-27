# Cashbox initialization and status recovery

A phone may request a cashbox status download before the manager has received MT38. Do not infer that status from call prices, CDR amounts or coin deposits. The existing combined upload dialogue can restore a known status; when the current box is unknown it must not invent a report.

## Initial status for a coin-capable phone

The public [mm_manager accounting reference](https://github.com/hharte/mm_manager/blob/main/src/mm_sqlite3.c) uses a 57-byte initial MT38 with the calendar sentinel 1990-01-01 at midnight and zeros for the remaining fields when no prior cashbox status exists. The sentinel is a starting protocol value, not an observed opening date or evidence of a physical empty box.

CoinLine provisions this initial status when a registered phone requests both INSTALL and cashbox status, has a published validated MT50 with at least one accepted positive-value coin, has no prior completed download, and has no cashbox report or initialization. It returns the default immediately after MT18 in the normal download dialogue. The separate initialization audit is idempotent and available for later MT42 uploads. A prior report or observed initialization takes precedence. A lost-memory request from an already commissioned phone does not silently reset accounting.

For an existing unreported coin phone being deliberately initialized, use **Initialize protocol cashbox default** in Cashbox. Confirm that there is no retained cashbox accounting to restore. This requires cashbox add permission and no active NCC session. It is unavailable once any real status or collection exists. The default is distinct from a reported balance and a collector event. A later operator-observed empty box or a genuine terminal report supersedes it. Do not use the default to erase a physical cashbox that has unaccounted funds.

## Observed empty replacement box

Use Cashbox status to record an explicitly observed empty replacement box if the ledger does not contain a current terminal report. An operator with add permission must confirm that the box is physically empty and enter its actual four-byte identifier as eight hexadecimal digits and its observed phone opening calendar including seconds. For a previously reported collection, the replacement identifier must match MT51. The phone must not have an active NCC session.

The manager records a distinct operator assertion, with its own audit actor and receipt time. It is not an MT38 received from the terminal, not a collection, and not an adjustment to a call record. Identical repeats return the same initialization and conflicting facts are rejected. A real subsequent status or collection supersedes it. When that new empty box is still the applicable starting state, the ordinary status request can return the corresponding zero-count MT38 with the supplied identifier and opening calendar. The combined MT42 response sends this complete status record before MT17 and identifies the source in the NCC session view, distinguishing the protocol default, operator observation and terminal report.

The initialization only establishes the observed opening state; it does not assert that the box is presently empty or that the terminal applied the offered status. Native device application and subsequent CDR/collection behavior remain a separate acceptance gate.

An invalid negative reported balance is not eligible for restoration. It does not automatically trigger the initial protocol default. If the actual box is independently verified empty, the explicit observed-empty procedure can recover its current status while retaining the invalid report as historical evidence. Otherwise leave the balance unknown until a valid terminal report arrives.
