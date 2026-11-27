# Operator-confirmed empty cashbox initialization

A phone may request a cashbox status download before the manager has received MT38. Do not infer that status from call prices, CDR amounts or coin deposits. The existing combined upload dialogue can restore a known status; when the current box is unknown it must not invent a report.

Use Cashbox status to record an explicitly observed empty replacement box if the ledger does not contain a current terminal report. An operator with add permission must confirm that the box is physically empty and enter its actual four-byte identifier as eight hexadecimal digits and its observed phone opening calendar including seconds. For a previously reported collection, the replacement identifier must match MT51. The phone must not have an active NCC session.

The manager records a distinct operator assertion, with its own audit actor and receipt time. It is not an MT38 received from the terminal, not a collection, and not an adjustment to a call record. Identical repeats return the same initialization and conflicting facts are rejected. A real subsequent status or collection supersedes it. When that new empty box is still the applicable starting state, the ordinary status request can return the corresponding zero-count MT38 with the supplied identifier and opening calendar. The combined MT42 response sends this complete status record before MT17 and identifies its source in the NCC session view.

The initialization only establishes the observed opening state; it does not assert that the box is presently empty or that the terminal applied the offered status. Native device application and subsequent CDR/collection behavior remain a separate acceptance gate.
