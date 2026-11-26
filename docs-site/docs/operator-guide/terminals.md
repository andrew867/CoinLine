# Terminals

1. Register terminal (`POST /api/terminals`) with site + terminal id hex.
2. Observe status (`POST .../status`).
3. Assign table set (`POST .../table-assignment` with confirms).

**UI:** `/terminals`, `/terminals/new`

Operators with an Add-only Phones profile can register new phones. The registration form offers existing sites, installed firmware and terminal families without opening the full catalogues. The optional group is available only with Membership Add and follows the selected site's customer. Registering a phone does not require permission to read existing phone records.

## Deactivation and reactivation

The phone detail page includes a Manager lifecycle panel. Deactivate an idle phone to record the date and a deletion review date; the default is 45 days from the current UTC date, and an operator can change it. The Phones list can filter active and inactive Manager records independently of the reported operational status.

Inactive phones remain identifiable in session records, but new NCC dialogues are closed before acknowledgement. They are excluded from new table assignment previews, scheduled or selected-record rollouts and new download batches. Existing assignments, pending schedules and stored history are retained. Reactivation clears both lifecycle dates and allows normal contact again.

Unlike the legacy Manager's automatic deletion on the specified date, CoinLine uses the grace date as a review date and does not automatically delete terminal or accounting records. Host deactivation does not remotely disable the terminal's hardware or guarantee that it stops attempting call-ins.

## Inmate terminal configuration

For a phone registered with the Inmate terminal family, the phone detail page has an Inmate terminal configuration panel. Select a named Inmate Feature Configuration and enter the external Inmate Setup Configuration ID supplied by the institution's separate setup system. Either reference can be left unassigned.

The references are saved in the Manager database. The Inmate Setup contents and the terminal record encodings are not available in the verified catalogue, so saving does not create a download. Changing the phone to another family leaves the references inactive and permits explicitly clearing them. A feature configuration cannot be deleted while a phone references it.

## Cashbox status

Open **Money > Cashbox status** to find a phone by name or NCC wire ID and browse the matching phones in wire-ID order. From a selected phone in **Terminal status**, choose **Query cashbox** to open the same view limited to that phone. Choose **Browse all cashboxes** to leave that selection.

The current reported box shows its balance, capacity percentage, condition, box number and reported coin counts. The manager receipt time is UTC; the phone's own reported clock is shown separately. Refresh reports only reads the manager's stored data and does not dial the phone. The collection history is separate: when a collection replaces the box, its balance does not establish the new box's contents. Wait for a new status report. A phone without reports is shown as such, not as having a zero balance.

Coin counts are shown in terminal slot order because the denomination mapping can vary with configured MT50. Unknown status bits are reported as unmapped rather than guessed. The screen requires Cashbox Read and does not provide any collection, money adjustment or configuration controls.
