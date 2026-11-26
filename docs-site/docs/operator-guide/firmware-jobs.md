# Firmware jobs

Firmware jobs is the place to prepare and review a proposed device update. A simulated job, a terminal's acknowledgement of a code schedule, a completed code transfer and a verified new installed version are different outcomes.

## Review a change of firmware version

1. Register the installed and proposed versions with their matching table profiles in the firmware registry.
2. In **Firmware jobs**, open **Review a firmware transition**. Select the phone and the target version. Compare the installed assignment first, or select a separate configuration set explicitly targeted to the proposed version.
3. Resolve records that need new lengths, source features that have no verified target equivalent, same-length layout warnings and outstanding management work. An acknowledged target record is evidence of delivery, not proof that its feature is mandatory or works.
4. Preserve the installed source assignment. Keep the proposed version's configuration separate until the actual device transition is qualified.

The comparison is read-only. It does not create a job, change an assignment, move the registered installed version, schedule a transfer or certify compatibility. See the [configuration transition review and qualification sequence](https://github.com/andrew867/CoinLine/blob/main/docs/program/firmware-configuration-transition.md) for the earlier MTR 2.0 and MTR 2.11-class example.

### Prepare the target management-access configuration

When the source and selected target set both contain an identified 18- or 48-byte MT21, the review offers **Prepare phone-specific management access**. Enter a name for a new draft and preview the carry-forward. The tool retains the source phone's registered wire ID and primary and alternate manager numbers, preserving the candidate's ADSI identifiers and remaining target fields. It does not display the access digits in the preview. The shorter target layout must fit both numbers; a malformed or ambiguous source is rejected rather than truncated.

Confirm to create a separate, unassigned draft. All other candidate records retain their bytes. Open the new draft and review the remaining source-to-target feature differences before publication. This step does not automatically migrate screening, card configuration, local exchanges, tariffs or other records, and does not create the upgrade job.

### Prepare the target screening configuration

When the source and candidate have the identified 200-entry and 180-entry MT92 records, **Prepare phone-specific screening rules** carries the source list into a separate target draft. It replaces the common fields and host descriptions at positions 1 through 180, including vacant and blanked entries; it never moves a rule to a different position. Candidate reserved bytes are retained. Source reserved data is not treated as later flags. If a changed candidate row has a later-only flag, resolve the conflict in the candidate before staging. Source entries, reserved bytes, descriptions or identified companion references beyond position 180 block the preview rather than disappearing.

For a source tariff reference, the preview offers only target MT73 rows with the identical shared classification, time periods and charges. An exact same index or a unique exact match is selected automatically; choose among multiple equivalent rows if needed. If none match, prepare the target tariff first. Carrier references are distinct from tariff indexes; choose a unique existing target carrier identifier and independently review its routing and acceptance settings. Free unrated calls retain their 255 sentinel.

Review the changed-position count, reference selections and findings, then confirm the new draft. Changing a reference requires a fresh preview. Other candidate records remain byte-for-byte unchanged, so review their pointers to screening positions, numbering rules, time discounts and the tariff effective date before publishing. Use the resulting draft as the next target candidate to continue preparation. This feature neither transfers a record to the phone nor qualifies the upgrade.

## Simulation and the native code scheduler

Use the simulation job form to exercise the host's checks without opening a device transport. Inspect the resulting job, safety checks and rollback notes before planning any real upgrade. The execution policy reports whether the existing live transport is enabled.

On supported terminals, a separate code-download scheduler record (MT94) is configured through table management. Its window, retry and cancellation settings are delivered through regular management contacts. A native code-server connection, image transfer, reboot and subsequent target configuration delivery must be qualified separately; a successful MT94 acknowledgement does not establish those outcomes.
