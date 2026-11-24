# Table distribution

The host stores table payloads with their length and SHA-256 digest. Payloads for the firmware download path must contain complete records whose leading message type matches their table definition. The catalog validator reports `EMPTY_PAYLOAD` for a zero-byte payload.

After the terminal's type 44 request, the listener prepares a batch from its assigned published set and sends a type 18 update message. It fragments records into NCC frames and waits for link acknowledgements between fragments. A type 14 application acknowledgement advances each complete record. The batch completes after the terminal link-acknowledges the final type 13 end marker.

The local firmware lab verified cold and warm ten-record sets. For other sets, check the actual terminal's acknowledgements and use the batch timeline to investigate missing or rejected frames. See [Downloads API](../api/endpoints/downloads.md).
