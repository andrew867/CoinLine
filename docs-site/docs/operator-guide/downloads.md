# Downloads

Open a phone to prepare its assigned full set or a selection of records. Use **Downloads** to review pending, running and finished batches. Select a batch to inspect its individual records; refresh its status while a transfer is in progress.

## Ordinary Craft refreshes

When a technician requests a download and no eligible scheduled or operator-prepared batch exists, CoinLine compares the currently assigned published configuration with the phone's last fully acknowledged configuration and subsequent acknowledged partial transfers. An ordinary Craft request (MT44 reason 01 or 81 hexadecimal) sends only changed or added records, including their declared dependencies, when the registered firmware and delivery history support that comparison. Identical bytes in a new published revision do not count as a changed record.

If nothing changed, the refresh sends the full assignment. Initial INSTALL, lost memory, interrupted or other request reasons, unknown firmware, removed records and uncertain delivery history also fall back to full delivery. A queued selection keeps its existing priority and original batch identity except for mandatory full installation recovery. Runtime rating and card authorization do not invoke this workflow. A removed record cannot be assumed erased by the full refresh; review the target profile and configuration before removing a record.

The audit log records the comparison baseline, decision and batch scope. A partial acknowledgement establishes delivery of the listed records, not proof of the remaining configuration or service behavior.

The detail page separates **host transfer evidence** from **terminal service verification**:

- Each record shows its message type, host phase, acknowledgement, failure reason if applicable, and payload hash.
- The acknowledgement count includes only records with a successful retained table acknowledgement. A missing link acknowledgement is different from a missing MT14 table acknowledgement, and records not sent are identified separately.
- When available, the linked NCC session shows its recorded start/end, the terminal's clear frame, and the observed transport release. Older sessions may not retain all of this evidence.
- A completed transfer by itself does not establish a successful INSTALL, working terminal peripherals, warm restart or service. Confirm the terminal display and actual service separately; the phone status link is a starting point, not an installation certificate.

An eligible pending batch can be cancelled before pickup. A retry prepares a replacement batch for a subsequent eligible terminal call, rather than altering the original history. Both actions request confirmation and require the appropriate operator permission.
