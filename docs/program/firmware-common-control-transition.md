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

The earlier MT34 carrier setting is mapped to the recognized later disabled or per-carrier mode, not copied as an entire control byte. A selected coin acceptance group must result in a positive accepted denomination and enabled validator window. A target that requires different hardware must have its own reviewed calibration; carrying coin values does not automatically change that calibration.

Review the selected controls, preview again, and create the separate unassigned draft only when the findings permit it. The review is bound to the exact configurations, selected groups, target version, scope and draft name. Changes after preview require a fresh review. The operation does not assign, publish, schedule a download, or initiate a firmware job.

The focused backend tests cover all six record lengths, selected groups, reserved bits, independent collection settings, preserved clock adjustments and coin hardware checks. A real PostgreSQL integration exercised permissions, stale reviews, draft creation and explicit publication. A separate registered loopback NCC fixture received and acknowledged exactly the six selected configuration records in its original partial batch. These results do not establish on-terminal behavior, hardware calibration, or actual upgrade compatibility.

Twelve retained demonstration records passed length and digest checks and unchanged native editor saves. Independent and combined group previews preserved candidate data and were repeatable. The six combined projections changed 0, 6, 5, 7, 0 and 0 bytes respectively in MT29, MT30, MT32, MT34, MT35 and MT50, with no blocking or advisory findings for these particular pairs. The zero-byte results reflect identical selected values, not an exercise of every setting. The synthetic unit and integration cases cover deliberate changes. The console build, eleven focused Vitest tests and two local Chromium transition flows passed.
