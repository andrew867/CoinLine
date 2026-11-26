# Cross-configuration screening entry parity

The native editor supports the legacy same-position add-to-all and delete-from-all operations with an explicit configuration scope and preview. It distinguishes a truly vacant slot from a blank number with retained call settings. The operation does not modify published source records or live phone assignments; it prepares separate drafts for published targets rather than deleting configuration history.

| Capability | Editor | Encoding and persistence | NCC acknowledgement | Terminal behavior |
| --- | --- | --- | --- | --- |
| Add saved entry to vacant slots | Preview, customer/shared or global scope, all compatible or draft-only | Compact and expanded unit vectors preserve destination spare bits, nearby rows and independent descriptions; PostgreSQL checks atomic conflict handling, stale revisions and replacement drafts. | Selected MT92 revision sent byte for byte on the original prepared batch in the loopback fixture. MT14 recorded on that batch. | Stock firmware behavior pending. |
| Delete the same position across configurations | Preview of in-use or referenced blockers; explicit confirmation | Known assigned/override/queued uses block all changes. Identified carrier, operator, IOPR and reload slot references are checked. Delete resets assigned settings without renumbering anything; original published sets and empty drafts are retained. | No download is implied by an editor action. | Stock firmware behavior pending. |
| Unsupported or ambiguous profile | Explicit excluded rows rather than guessing | Exact type, length and compatible layout checked independently per set; ambiguous same-length MT59 requires its own known layout. | Not attempted. | Pending profile qualification. |
