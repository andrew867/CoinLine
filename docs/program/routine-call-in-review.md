# Routine call-in configuration and evidence

Open a phone in the console and review its Routine call-in details panel. The panel distinguishes three independent observations:

- **Manager assignment:** the MT35 record in the currently assigned table set.
- **Last acknowledged record:** the latest retained successful MT14 for an MT35 version on that phone. A later failed or incomplete attempt is separately flagged.
- **Routine contact:** the latest retained, integrity-checked, non-retransmitted MT8 on the phone's current registered ID. A callback or an INSTALL request is not a routine-contact observation.

The panel shows the programmed call-in date and time, repeat interval, retry delay, call-record threshold and optional clock adjustment. The two-digit start year and phone-local clock are displayed as stored. The host does not infer a next UTC contact time or guarantee that the terminal used the schedule.

Open the assigned MT35 editor to review the native fields. For an active phone, an operator with table-set Add and table-record Update permissions can create a separate draft of the assigned set. The draft opens at MT35 and preserves existing record bytes until a field is edited. It does not modify the phone or prepare a transfer. Publish the reviewed copy. The selected-record delivery view preselects that phone and MT35 when it is valid, and requires explicit preview and confirmation; if MT35 is missing or invalid, it does not default to all records. A selected-record schedule is not a reassignment. To make the edit the desired configuration for future full downloads, review and apply the new assignment or use the selected-record assignment workflow.

Assigned settings require both table-assignment and table-record Read permission. The acknowledged record, batch links and pending counts additionally require Downloads Read; the routine contact requires Phone sessions Read. Restricted evidence is not returned.

## Acceptance record

| Workflow | Editor | Encoding | Terminal application ACK | Observed behavior |
| --- | --- | --- | --- | --- |
| Routine call-in details, MT35 | Phone-specific comparison, source links and separate draft creation in the native console | Existing 21-byte MT35 editor; clone preserves exact bytes | Read-only evidence distinguishes retained per-record ACK from a queued or uncertain transfer | Retained MT8 is distinguished from callbacks and retransmits. Earlier emulated routine call-in acceptance remains documented separately; this panel does not certify new stock-terminal timing behavior. |
