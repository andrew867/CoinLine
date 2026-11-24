# Terminals

1. Register terminal (`POST /api/terminals`) with site + terminal id hex.
2. Observe status (`POST .../status`).
3. Assign table set (`POST .../table-assignment` with confirms).

**UI:** `/terminals`, `/terminals/new`

## Inmate terminal configuration

For a phone registered with the Inmate terminal family, the phone detail page has an Inmate terminal configuration panel. Select a named Inmate Feature Configuration and enter the external Inmate Setup Configuration ID supplied by the institution's separate setup system. Either reference can be left unassigned.

The references are saved in the Manager database. The Inmate Setup contents and the terminal record encodings are not available in the verified catalogue, so saving does not create a download. Changing the phone to another family leaves the references inactive and permits explicitly clearing them. A feature configuration cannot be deleted while a phone references it.
