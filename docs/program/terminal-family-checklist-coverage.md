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

## Registered family and payment mode

The original terminal information workflow expects the registered terminal family and the Universal Feature configuration to agree. The inventory type number identifies the release and hardware family; it is not the three-value MT26 operating-mode field. The checklist therefore compares the documented family expectation (Card only, Multi-pay or Coin only) with the saved MT26 value when the registered target establishes the exact native layout. Desk and Inmate are Card-family references; Coin Basic and the Multi-pay-based stored-value families have their corresponding ordinary payment-mode expectations. This comparison does not establish optional card-reader, PIN, purse, carrier or coin capabilities.

The comparison appears in the set checklist, individual phone worksheet and its proposed assignment, guided draft setup, and firmware-transition candidate review. Missing records, duplicates, invalid codes, unknown or length-only target layouts and intentional differences are separate states. A partial set may rely on an installed MT26. A mismatch is advisory: a configured payment restriction can be intentional, and no record is rewritten or assignment blocked by this check. Review the actual coin and card acceptance, routes and payment policy independently before delivery.
