# NCC sessions API

**Route prefix:** `/api/ncc/sessions`

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/ncc/sessions` | List NCC sessions |
| GET | `/api/ncc/sessions/{id}` | Inspect a session and its received frames |
| GET | `/api/ncc/sessions/{id}/capture` | Download a redacted Wireshark capture |

On **NCC sessions**, use **Download redacted capture** for the session you want to inspect. The PCAPNG file retains the session frame order, UTC timestamps, direction, available NCC control bytes, and on-wire lengths. Its packet comments show these fields in Wireshark. Each packet contains at most the control byte. Terminal identifiers, correlation IDs, CRCs and all application payload bytes are omitted.

The export covers accepted terminal frames and successful host writes. Older sessions contain received frames only. Older entries whose raw bytes were withheld still have a timestamp and sequence, with the header shown as unavailable. This capture supports link sequencing and timing analysis. [PCAPNG Enhanced Packet Block format](https://datatracker.ietf.org/doc/html/draft-ietf-opsawg-pcapng-06#section-4.3)
