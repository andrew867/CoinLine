# Terminal family checklist coverage

The configuration set checklist and individual phone configuration worksheet review recorded table families against the selected terminal family. This is an advisory review of the host assignment, not an INSTALL compatibility certificate or a substitute for device acceptance.

The shared MTR 2.0 terminal help topic explicitly includes Desk type 37 and identifies coin validation as mandatory for Multi-pay and Coin Basic. The Desk review therefore uses that shared checklist without requiring coin validation. A separate Desk-specific wire profile has not been established.

## Screening and prompts

- The documented screening list contains either 50 or 200 entries. Packed record capacities can differ from that logical limit. A known target length may establish a different capacity; a competing or shorter unqualified layout is shown as needing review.
- The checklist distinguishes recognized candidates from unverified records, missing records, incomplete groups and wrong record formats.
- Where the target catalogue establishes a primary and secondary prompt pair, both compatible members are needed for a complete prompt group. The checklist does not require unestablished additional languages.
- A partial update may intentionally contain only a subset of the families or one member of a group. Review results do not prevent publishing a partial configuration set or imply that an installed companion is absent.

## Family-aware screening workspace

Select a terminal family in the checklist on the configuration set page. The screening editor uses that review context to show the documented logical entry count alongside the exact packed record capacity. For a 50-entry family, the default view shows only the documented entries; **All wire slots** always makes the complete record available. When the documented count exceeds the actual packed capacity, every available slot remains visible and the editor identifies the discrepancy.

The MSR 1.7 Inmate workflow reserves entries 16 through 50 for the separate institutional configuration client. The ordinary operator view shows entries 1 through 15. Separate institutional and complete-wire views remain available for inspecting imported configurations. The workspace identifies saved non-vacant settings and descriptions in institutional or extra slots, including entries that were blanked rather than deleted. Unassigned reserved bits alone do not count as configured entries.

Selecting a family or switching views is a read-only review choice: it does not assign a terminal type, change the target firmware or record bytes, remove descriptions, discard staged field edits, or qualify the terminal's actual active capacity. The institutional partition is an authoring aid, not an enforcement rule or a replacement for the institution's separate setup workflow. Review populated entries before making intentional changes.

An isolated PostgreSQL regression opened the same 60-slot compact record under an ordinary, institutional and later family context. The unchanged save retained its digest and revision; editing answer supervision changed exactly one bit while the institutional, extra-slot and unassigned spare data remained intact. Browser component tests verified the three views, retention of staged edits, shorter-capacity reporting and published read-only access. Edited-record terminal acknowledgement and institutional behavior are not established by these checks.

Editor recognition, exact byte preservation, terminal acknowledgement and a demonstrated change in behavior remain separate acceptance criteria.
