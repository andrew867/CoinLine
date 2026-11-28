# Common configuration carry-forward

Use **Firmware jobs**, **Review a firmware transition** to compare an earlier installed configuration with an independently prepared later configuration. The review supports registered earlier 200-entry and later 180-entry Multi-Pay layouts, including supported observed-length profiles. This is preparation, not a declaration of version or hardware compatibility.

Open **Prepare common configuration controls** and preview the records before selecting anything. No group is selected by default. Missing or incompatible records cannot be selected. Expand any group to compare source and target values; unchanged values can also be shown. The target configuration remains the authoritative template for unselected settings and reserved data.

| Record | Independent selections | Retained unless explicitly reviewed |
| --- | --- | --- |
| MT29 | Text rotation, duration, mode and blanking | Reserved slot bits, spare bytes, dormant text, graphical artwork and display enablement |
| MT30 | Prompt/input timing; card/rating/dialing timers; assisted carrier timing; data port timing | Unselected timing groups and spare bytes |
| MT32 | Communications statistics collection and interval controls | Reserved timing pair |
| MT34 | Call statistics/CDR collection; carrier statistics mode | Later-specific and reserved bits; combined carrier summary unless selected |
| MT35 | Routine call-in time of day, interval, retries and upload threshold | Target start date and one-time clock adjustment |
| MT50 | Coin values/acceptance; validator windows/physical capacities; cashbox/escrow report thresholds | Unselected calibration, spare bytes and existing cashbox accounting |
| MT58 | Earlier daily service restriction level, phone-local start and duration | Candidate reserved bytes and the independently configured restricted-route permissions in other records |
| MT93 | Individual unit index, maximum numeric units and unit multiplier | Every unselected index, existing validation data, discounts, reserved regions, card-value and issuer associations |

The earlier MT34 carrier setting is mapped to the recognized later disabled or per-carrier mode, not copied as an entire control byte. A selected coin acceptance group must result in a positive accepted denomination and enabled validator window. A target that requires different hardware must have its own reviewed calibration; carrying coin values does not automatically change that calibration.

For MT58, the earlier supported levels and valid daily window are carried only when explicitly selected. An invalid earlier schedule blocks creation; a candidate's later-only configurable restriction level is replaced only by this reviewed selection. Its independent MT55, MT92 and graphical allowed-route settings are not edited or automatically enabled by the transition.

For MT93, the fifteen common unit indexes are reviewed independently. Each selected index carries the two defined packed values without modifying another index or the candidate's different earlier/later configuration regions. Review the intended target card-value association before selecting an index: neither the association, issuer configuration nor any validation data is migrated. This does not personalize a card, change its balance or qualify stored-value payment.

Review the selected controls, preview again, and create the separate unassigned draft only when the findings permit it. The review is bound to the exact configurations, selected groups, target version, scope and draft name. Changes after preview require a fresh review. The operation does not assign, publish, schedule a download, or initiate a firmware job.

The sixteen focused backend cases cover the common record layouts, selected groups, reserved bits, independent collection settings, preserved clock adjustments, coin hardware checks, daily service restrictions and the independent MT93 unit pairs. A real PostgreSQL integration exercised permissions, stale reviews, draft creation and explicit publication. A separate registered loopback NCC fixture received and acknowledged exactly eight selected configuration records, including the prepared MT93, in its original partial batch. The fixture confirms that the selected unit pair changes while the other candidate bytes are retained. The updated WebUI passed six focused Vitest cases and the production build. These results do not establish on-terminal behavior, hardware calibration, stored-value application acceptance or actual upgrade compatibility.

Twelve retained demonstration records passed length and digest checks and unchanged native editor saves. Independent and combined group previews preserved candidate data and were repeatable. The six combined projections changed 0, 6, 5, 7, 0 and 0 bytes respectively in MT29, MT30, MT32, MT34, MT35 and MT50, with no blocking or advisory findings for these particular pairs. The zero-byte results reflect identical selected values, not an exercise of every setting. The synthetic unit and integration cases cover deliberate changes. The console build, eleven focused Vitest tests and two local Chromium transition flows passed.
