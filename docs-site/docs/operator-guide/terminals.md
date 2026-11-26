# Terminals

1. Register terminal (`POST /api/terminals`) with site + terminal id hex.
2. Observe status (`POST .../status`).
3. Assign table set (`POST .../table-assignment` with confirms).

**UI:** `/terminals`, `/terminals/new`

Operators with an Add-only Phones profile can register new phones. The registration form offers existing sites, installed firmware and terminal families without opening the full catalogues. The optional group is available only with Membership Add and follows the selected site's customer. Registering a phone does not require permission to read existing phone records.

## Latest reported phone status

Open **Terminal status**, select a phone by name and wire ID, and inspect **Latest reported phone status**. The panel shows the serial number contained in the last valid MT10, the Manager receipt time, and named status conditions grouped by hardware, memory, enclosure, cash box and line equipment. Separate labels distinguish a reported condition from one not indicated in that snapshot. An unmapped bit is called out rather than treated as clear. An unreadable later report does not erase the earlier valid report; its receipt time is shown separately.

These are terminal observations from a matched NCC session, not a live hardware or health check. A phone with no valid report is distinguished from one reporting no named conditions. Inventory deactivation, last contact, event history and the latest status capture are separate observations. A status-only operator can use this panel without opening the phone registry or detailed message log. With message-log read access, the source status report is linked.

## Deactivation and reactivation

The phone detail page includes a Manager lifecycle panel. Deactivate an idle phone to record the date and a deletion review date; the default is 45 days from the current UTC date, and an operator can change it. The Phones list can filter active and inactive Manager records independently of the reported operational status.

Inactive phones remain identifiable in session records, but new NCC dialogues are closed before acknowledgement. They are excluded from new table assignment previews, scheduled or selected-record rollouts and new download batches. Existing assignments, pending schedules and stored history are retained. Reactivation clears both lifecycle dates and allows normal contact again.

Unlike the legacy Manager's automatic deletion on the specified date, CoinLine uses the grace date as a review date and does not automatically delete terminal or accounting records. Host deactivation does not remotely disable the terminal's hardware or guarantee that it stops attempting call-ins.

## Inmate terminal configuration

For a phone registered with the Inmate terminal family, the phone detail page has an Inmate terminal configuration panel. Select a named Inmate Feature Configuration and enter the external Inmate Setup Configuration ID supplied by the institution's separate setup system. Either reference can be left unassigned.

The references are saved in the Manager database. The Inmate Setup contents and the terminal record encodings are not available in the verified catalogue, so saving does not create a download. Changing the phone to another family leaves the references inactive and permits explicitly clearing them. A feature configuration cannot be deleted while a phone references it.
