# Service boundaries

| Boundary | Inside | Outside |
|----------|--------|---------|
| PCI cardholder data | Upstream PCI zone / HSM — **not** persisted as full track/PAN in host APIs | Raw mag payloads stored only where policy allows; **`SIMULATION ONLY`** defaults |
| Modem transport | Interface placeholders (`IDlXmodemTransportAdapter`) — **not implemented** | PSTN timing, carrier loss |
| Firmware flash | Registry + job rows + simulation transitions | On-terminal EEPROM programming |
| Protocol decode | Classifiers + registries | Full semantic parity with every SKU |
