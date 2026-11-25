# NCC valid fixtures

## `clr_sample.*`

MTR212-style **CLR** control frame (6 octets: STX, control, count, CRC LE, ETX). CRC matches the first three bytes per `NccCrc16`.

- **`clr_sample.hex`**: uppercase hex string without spaces (same octets as `.bin`).
- **`clr_sample.expected.json`**: smoke expectations for automated comparison (`byteLength`, `frameCount`).
- **`clr_sample.bin`**: raw UART octets.

Use with:

- `ncc-replay decode fixtures/ncc/valid/clr_sample.bin`
- API `POST /api/ncc/decode` with `rawHex` from `clr_sample.hex`.
