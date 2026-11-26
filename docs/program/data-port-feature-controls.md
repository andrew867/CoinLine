# Data port feature controls

The MT26 Feature Options editor exposes the complete data-port control byte for the supported 72-byte Multi-Pay layouts. The byte is at zero-based record offset 54, including the message-type byte.

| Mask | Editor setting |
| ---: | --- |
| 0x01 | Enable data port calls |
| 0x02 | Mute handset transmitter immediately |
| 0x04 | Allow free local calls on the data port |
| 0x08 | Allow directory assistance on the data port |
| 0x10 | Disable keypad during connected data calls |
| 0x20, 0x40, 0x80 | Unidentified spare flags 5, 6 and 7 |

The five named options appear separately from the three explicit spare flags. Normal feature edits retain the spare flags, display delay, answer-supervision settle time and other fields. Spare flags have no assigned behavior in the editor; preserve their installed values unless an applicable terminal profile establishes a reason to change them.

The feature enable does not replace screening rules: individual entries can still deny data-port calls. MT30 and MT33 contain separate data-port timing parameters. Configuration, application acknowledgement, and an observed data-port call are independent verification steps; changed behavior on an equipped terminal remains pending.
