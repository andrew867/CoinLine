# Firmware jobs

Firmware jobs is the place to prepare and review a proposed device update. A simulated job, a terminal's acknowledgement of a code schedule, a completed code transfer and a verified new installed version are different outcomes.

## Review a change of firmware version

1. Register the installed and proposed versions with their matching table profiles in the firmware registry.
2. In **Firmware jobs**, open **Review a firmware transition**. Select the phone and the target version. Compare the installed assignment first, or select a separate configuration set explicitly targeted to the proposed version.
3. Resolve records that need new lengths, source features that have no verified target equivalent, same-length layout warnings and outstanding management work. An acknowledged target record is evidence of delivery, not proof that its feature is mandatory or works.
4. Preserve the installed source assignment. Keep the proposed version's configuration separate until the actual device transition is qualified.

The review is read-only. It does not create a job, change an assignment, move the registered installed version, schedule a transfer or certify compatibility. See the [configuration transition review and qualification sequence](https://github.com/andrew867/CoinLine/blob/main/docs/program/firmware-configuration-transition.md) for the earlier MTR 2.0 and MTR 2.11-class example.

## Simulation and the native code scheduler

Use the simulation job form to exercise the host's checks without opening a device transport. Inspect the resulting job, safety checks and rollback notes before planning any real upgrade. The execution policy reports whether the existing live transport is enabled.

On supported terminals, a separate code-download scheduler record (MT94) is configured through table management. Its window, retry and cancellation settings are delivered through regular management contacts. A native code-server connection, image transfer, reboot and subsequent target configuration delivery must be qualified separately; a successful MT94 acknowledgement does not establish those outcomes.
