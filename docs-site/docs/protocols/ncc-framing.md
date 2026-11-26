# NCC framing

- **Codec:** `NccFrameCodec` — **strict** decode vs **gap-preserving diagnostic** decode paths for inspection; CRC-16 uses **in-product** generator tables aligned to supported integrations.
- **Gap preservation:** diagnostic modes retain ambiguous octets for engineering review — **not** a substitute for interchange certification without **field validation**.

The TCP listener strictly decodes incoming frames. It resolves the five-byte address against the terminal catalog and stores incoming frames with the session and packet order. A link control frame carries its packet ID. The persisted session retains the terminal association across those control frames.

A valid terminal-initiated clear ends the application dialogue immediately, but not necessarily the terminal's modem release sequence. CoinLine unregisters the link for outbound application work, sends no additional frame, and leaves the TCP transport open for the peer to release. It closes on peer EOF or a bounded drain deadline (three seconds by default). Later bytes on the cleared link are discarded without dispatch or acknowledgement. An unexpected EOF or read failure without a valid clear follows the ordinary disconnect path. The redacted `terminal_clear_drain` audit records the outcome, elapsed host time and discarded byte count. It is not a measurement of physical carrier.

For the firmware clock exchange, a type 36 request receives type 20 followed by type 13. Both responses are paced by link acknowledgements. A matching NACK retransmits the pending response with the same packet ID and the retransmit bit set. In matched firmware dialogues the host waits 350 ms before its link ACK so the terminal can finish transmitting and arm its reply timer.

The listener handles complete maintenance and alarm uploads with the corresponding type 15 and 16 acknowledgements and a type 13 end marker. Install and rating have their own application dialogues. See [Table distribution](table-distribution.md) and [Rating and call flow](rating-call-flow.md).

Refer to [Compatibility overview](compatibility-overview.md) for customer-facing scope statements.
