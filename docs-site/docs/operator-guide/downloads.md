# Downloads

Open a phone to prepare its assigned full set or a selection of records. Use **Downloads** to review pending, running and finished batches. Select a batch to inspect its individual records; refresh its status while a transfer is in progress.

The detail page separates **host transfer evidence** from **terminal service verification**:

- Each record shows its message type, host phase, acknowledgement, failure reason if applicable, and payload hash.
- The acknowledgement count includes only records with a successful retained table acknowledgement. A missing link acknowledgement is different from a missing MT14 table acknowledgement, and records not sent are identified separately.
- When available, the linked NCC session shows its recorded start/end, the terminal's clear frame, and the observed transport release. Older sessions may not retain all of this evidence.
- A completed transfer by itself does not establish a successful INSTALL, working terminal peripherals, warm restart or service. Confirm the terminal display and actual service separately; the phone status link is a starting point, not an installation certificate.

An eligible pending batch can be cancelled before pickup. A retry prepares a replacement batch for a subsequent eligible terminal call, rather than altering the original history. Both actions request confirmation and require the appropriate operator permission.
