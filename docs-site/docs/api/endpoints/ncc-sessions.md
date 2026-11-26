# NCC sessions API

**Route prefix:** `/api/ncc/sessions`

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/ncc/sessions` | List NCC sessions |
| GET | `/api/ncc/sessions/{id}` | Inspect a session and its received frames |
| GET | `/api/ncc/sessions/{id}/upload-observations` | Inspect recognized call-upload record boundaries without payloads |
| GET | `/api/ncc/sessions/{id}/capture` | Download a redacted Wireshark capture |

On **NCC sessions**, use **Download redacted capture** for the session you want to inspect. The PCAPNG file retains the session frame order, UTC timestamps, direction, available NCC control bytes, and on-wire lengths. Its packet comments show these fields in Wireshark. Each packet contains at most the control byte. Terminal identifiers, correlation IDs, CRCs and all application payload bytes are omitted.

Use **Inspect upload records** to review the explicit call-upload frames observed after an MT42 request and MT17 host response. The observation response includes `callUploadFrames`, each with the incoming frame sequence, whether all record boundaries were recognized, the end-of-data marker if known, and the recognized message types and byte counts in wire order. An unrecognized or incomplete frame has no record list and an unknown end marker. This identifies whether MT38 cashbox status or MT51 collection was recognized independently of a coin call record, without retaining uploaded values, card details, dialed digits or raw payloads. The observation describes parsing, not persistence or completion. Older sessions may have no retained observations, which does not prove that a record was absent.

The export covers accepted terminal frames and successful host writes. Older sessions contain received frames only. Older entries whose raw bytes were withheld still have a timestamp and sequence, with the header shown as unavailable. This capture supports link sequencing and timing analysis. [PCAPNG Enhanced Packet Block format](https://datatracker.ietf.org/doc/html/draft-ietf-opsawg-pcapng-06#section-4.3)
