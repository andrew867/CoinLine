# Firmware jobs

Firmware jobs is the place to prepare and review a proposed device update. A simulated job, a terminal's acknowledgement of a code schedule, a completed code transfer and a verified new installed version are different outcomes.

## Review a change of firmware version

1. Register the installed and proposed versions with their matching table profiles in the firmware registry.
2. In **Firmware jobs**, open **Review a firmware transition**. Select the phone and the target version. Compare the installed assignment first, or select a separate configuration set explicitly targeted to the proposed version.
3. Resolve records that need new lengths, source features that have no verified target equivalent, same-length layout warnings and outstanding management work. An acknowledged target record is evidence of delivery, not proof that its feature is mandatory or works.
4. Preserve the installed source assignment. Keep the proposed version's configuration separate until the actual device transition is qualified.

The comparison is read-only. It does not create a job, change an assignment, move the registered installed version, schedule a transfer or certify compatibility. See the [configuration transition review and qualification sequence](https://github.com/andrew867/CoinLine/blob/main/docs/program/firmware-configuration-transition.md) for the earlier MTR 2.0 and MTR 2.11-class example.

### Prepare target terminal tariffs

For the identified earlier 200-entry and later 180-entry terminal families, **Prepare phone-specific terminal tariffs** reviews the common 128-row MT73 tariff layout. All identical source tuples share a target definition, including invalid and unavailable rates. Existing exact tuples are reused. For a missing tuple, the tool proposes an empty invalid or unavailable target row outside 0 through 3, provided no included candidate record references that index. A nonzero invalid/unavailable tuple is not treated as empty. Active target tariffs are never replaced.

Review the source indexes, classification, exact periods and charges, proposed target position and allocation action. Open **Protected target indexes** to see the relevant included record types. This review conservatively protects numeric fields even where a feature may be disabled. Unreviewed candidate records prevent all new allocation; missing companion records are identified for future review. Choose another eligible position if needed, preview again and confirm a separate draft. The target effective date and reserved header remain unchanged. Use the displayed source-to-target indexes when preparing screening and local exchanges; this tool does not automatically translate those references or establish device rating behavior.

### Prepare the target management-access configuration

When the source and selected target set both contain an identified 18- or 48-byte MT21, the review offers **Prepare phone-specific management access**. Enter a name for a new draft and preview the carry-forward. The tool retains the source phone's registered wire ID and primary and alternate manager numbers, preserving the candidate's ADSI identifiers and remaining target fields. It does not display the access digits in the preview. The shorter target layout must fit both numbers; a malformed or ambiguous source is rejected rather than truncated.

Confirm to create a separate, unassigned draft. All other candidate records retain their bytes. Open the new draft and review the remaining source-to-target feature differences before publication. This step does not automatically migrate screening, card configuration, local exchanges, tariffs or other records, and does not create the upgrade job.

## Simulation and the native code scheduler

Use the simulation job form to exercise the host's checks without opening a device transport. Inspect the resulting job, safety checks and rollback notes before planning any real upgrade. The execution policy reports whether the existing live transport is enabled.

On supported terminals, a separate code-download scheduler record (MT94) is configured through table management. Its window, retry and cancellation settings are delivered through regular management contacts. A native code-server connection, image transfer, reboot and subsequent target configuration delivery must be qualified separately; a successful MT94 acknowledgement does not establish those outcomes.
