# Terminals

1. Register terminal (`POST /api/terminals`) with site + terminal id hex.
2. Observe status (`POST .../status`).
3. Assign table set (`POST .../table-assignment` with confirms).

**UI:** `/terminals`, `/terminals/new`

## Individual configuration assignments

From a phone's **Table assignment** section, expand **Individual configuration assignments**. The worksheet lists its current Manager configuration IDs and revisions by family and record type. Use the family filter or search to find a row, including an unassigned record available for the registered firmware. The legacy mandatory-family review is advisory; a listed family is not proof of an accepted INSTALL.

Choose **Look up MT…** to find published records by Configuration ID or set name. The lookup includes shared configurations and those owned by the phone's customer, with firmware or record incompatibilities shown alongside the options. Operators with read-only assignment access can browse; full record inspection requires the independent table set and record read permissions.

An assignment operator can select records from different published sets, one choice per message type, and explicitly clear an optional record when removal is confirmed for the installed profile. The default for every other record is **Leave unchanged**. Select a source MT21 and explicitly enable the registered-wire-ID option if its identity must be personalized. Preview the resulting set; required dependencies are included and conflicting source requirements are rejected. Review missing legacy families before applying, especially when assembling an initial assignment.

Apply creates one new published host assignment while retaining the prior set for rollback. No other phone, source configuration or pending download is changed. A phone with queued or running configuration delivery must complete or cancel that work first. Clearing an optional assignment does not erase a record already in terminal memory. Use the separate download workflow to review and deliver the new configuration to the device.

Operators with an Add-only Phones profile can register new phones. The registration form offers existing sites, installed firmware and terminal families without opening the full catalogues. The optional group is available only with Membership Add and follows the selected site's customer. Registering a phone does not require permission to read existing phone records.

## Latest reported phone status

Open **Terminal status**, select a phone by name and wire ID, and inspect **Latest reported phone status**. The panel shows the serial number contained in the last valid MT10, the Manager receipt time, and named status conditions grouped by hardware, memory, enclosure, cash box and line equipment. Separate labels distinguish a reported condition from one not indicated in that snapshot. The current registered profile identifies earlier or later status meanings; it does not independently establish historical firmware. Unresolved asserted bits display their exact bit index and mask, and the five reported mask bytes remain visible. An unreadable later report does not erase the earlier valid report; its receipt time is shown separately. Opening an MT10 report displays the same human-readable groups, with the original JSON and raw bytes under **Technical record**.

These are terminal observations from a matched NCC session, not a live hardware or health check. A phone with no valid report is distinguished from one reporting no named conditions. Inventory deactivation, last contact, event history and the latest status capture are separate observations. A status-only operator can use this panel without opening the phone registry or detailed message log. With message-log read access, the source status report is linked.

## Deactivation and reactivation

The phone detail page includes a Manager lifecycle panel. Deactivate an idle phone to record the date and a deletion review date; the default is 45 days from the current UTC date, and an operator can change it. The Phones list can filter active and inactive Manager records independently of the reported operational status.

Inactive phones remain identifiable in session records, but new NCC dialogues are closed before acknowledgement. They are excluded from new table assignment previews, scheduled or selected-record rollouts and new download batches. Existing assignments, pending schedules and stored history are retained. Reactivation clears both lifecycle dates and allows normal contact again.

Unlike the legacy Manager's automatic deletion on the specified date, CoinLine uses the grace date as a review date and does not automatically delete terminal or accounting records. Host deactivation does not remotely disable the terminal's hardware or guarantee that it stops attempting call-ins.

## Inmate terminal configuration

For a phone registered with the Inmate terminal family, the phone detail page has an Inmate terminal configuration panel. Select a named Inmate Feature Configuration and enter the external Inmate Setup Configuration ID supplied by the institution's separate setup system. Either reference can be left unassigned.

The references are saved in the Manager database. The Inmate Setup contents and the terminal record encodings are not available in the verified catalogue, so saving does not create a download. Changing the phone to another family leaves the references inactive and permits explicitly clearing them. A feature configuration cannot be deleted while a phone references it.

## Cashbox status

Open **Money > Cashbox status** to find a phone by name or NCC wire ID and browse the matching phones in wire-ID order. From a selected phone in **Terminal status**, choose **Query cashbox** to open the same view limited to that phone. Choose **Browse all cashboxes** to leave that selection.

When the manager has received neither a cashbox status (MT38) nor a collection (MT51) report, the balance is unknown, not zero. A successfully uploaded coin call can still have a call record without a cashbox report. Its charge and collected amount do not establish the current box balance. Review the reports received from the phone and use **Refresh reports** to reread the manager's stored data; this does not request a new report from the phone.

The current reported box shows its balance, capacity percentage, condition, box number and reported coin counts. The manager receipt time is UTC; the phone's own reported clock is shown separately. Refresh reports only reads the manager's stored data and does not dial the phone. The collection history is separate: when a collection replaces the box, its balance does not establish the new box's contents. Wait for a new status report. A phone without reports is shown as such, not as having a zero balance.

Coin counts are shown in terminal slot order because configured denominations can vary. If the manager recorded an MT50 application acknowledgement for the same phone before receiving the report, the configured positive slot values are shown with the acknowledged revision and receipt time. They are a configuration reference, not proof that accumulated coins were counted under that configuration. A later transfer without a confirmed acknowledgement, missing, ambiguous or unreadable MT50 evidence leaves the raw slots visible without a value label. Operators with Downloads Read can also open the source download. Unknown status bits are reported as unmapped rather than guessed. The screen requires Cashbox Read and does not provide any collection, money adjustment or configuration controls.
