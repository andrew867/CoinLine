# NCC malformed-wire fixtures

Golden inputs for strict-parse rejection vs diagnostic capture (see `HostPlatform.Tests.Protocol`).

| File | Description |
|------|-------------|
| `crc_mismatch_min.bin` | Minimal CLR-class frame with deliberately wrong CRC tail (`0xFF,0xFF`) — strict decode fails; diagnostic capture retains octets with diagnostics. |

Wire layout: `STX`, CLR control (`0x20`), byte count `0x05`, bad CRC low/high, `ETX`.
