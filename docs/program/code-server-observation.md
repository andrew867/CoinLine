# Native code-server observation

The MT94 Manager scheduling workflow prepares, delivers and records the acknowledgement of code-download parameters. The subsequent independent code-server call is a different protocol conversation. Parameter acknowledgement does not prove the call occurred, an image was transferred, or the terminal rebooted into the requested version.

The existing XMODEM sender is tested separately using generated payloads and scripted peers. It requires a receiver negotiation byte before writing a block, distinguishes a closed transport from a response timeout, reports receiver cancellation, and repeats a rejected block without advancing its sequence or payload progress. An acknowledged EOT establishes transport completion only. The firmware job still requires independent terminal version and configuration confirmation; this sender does not establish that the native code-server call uses this exact transfer sequence.

An independent bounded XMODEM-128 receiver now supports the opposite direction for an explicitly selected duplex session. It negotiates checksum or CRC mode, checks sequence and complement, limits blocks and session time, accepts exact duplicate retries without appending them twice, and withholds incomplete or invalid results. The caller receives block-rounded bytes and must validate the application record and its padding separately. Generated-data tests do not establish native request layout, authorization, application acknowledgement or successful code delivery. No incoming listener is enabled by this component.

For an isolated laboratory phone whose normal INSTALL and restart have been verified, the receive-only observer provides a first test of the independent call route. It listens on IPv4 loopback only, handles one connection at a time and stops after its configured lifetime. Each connection has independent idle, total-time and received-byte limits. It never sends an image, a greeting or any application data.

```sh
python3 tools/code_server_observer.py --port 4001 \
  --duration-seconds 900 --session-seconds 60 --idle-seconds 15 \
  --output code-server-observation.jsonl
```

Use a distinct lab-only code-server modem alias routed to this listener. Keep ordinary NCC access and the phone's existing data assignment separate. The output file must be new. Its JSON lines record connection events, UTC timestamps, receive counts and possible standalone control octets. No arbitrary payload or peer identity is retained. A TCP receive boundary is not necessarily a protocol frame; a possible control classification alone is not proof of a specific protocol. Keep the report local to the bench.

The next acceptance gates are an observed native handshake, documented compatible transfer framing, an independently approved target image and isolated code-download parameters. Actual image delivery, device verification, reboot and interrupted-transfer recovery are not established by this observer. Do not direct an operator's in-service phone at the bench endpoint.

The tests use only loopback synthetic data. They check that the listener sends zero bytes, that arbitrary input is absent from its report, that idle connections close and that the receive cap applies.

## Native scheduling acceptance

A previously installed MTR 2.12 graphical lab terminal completed a normal Craft refresh. Its MT44 Craft and cashbox request received all eighteen assigned data records; the separate MT94 schedule stayed pending. After that schedule's start expired, the host cancelled it and prepared a replacement containing one 100-byte MT94, with one requested 4 KiB block and blank authentication. The normal assignment stayed unchanged.

On the next ordinary warm management contact, the terminal acknowledged an immediate MT19 special callback, returned MT9, and picked up the original separately prepared MT94 batch. It returned the application MT14, and the host completed the batch with one of one records acknowledged before normal link closure. The scheduled record contained the selected target and application revisions, 2400 baud, and exactly one selected block; no unrelated configuration record was sent on the callback.

After the configured start, the independently routed code-server listener accepted a genuine native terminal connection. The peer closed after approximately 3.7 seconds without sending data. The observer sent zero bytes. The native trace confirmed the independent dial and relay release, but did not establish asserted carrier during that connection. The subsequent separate NCC contact did show carrier. The saved flash remained unchanged. This verifies scheduling, native application acknowledgement and independent connection routing, not a completed modem dialogue. Zero bytes do not distinguish a server-first application conversation from a carrier-stage failure. Transfer, image compatibility, flash programming, recovery and reboot remain unverified.

The observer was stopped after the first native connection. The delivered scheduling history was retained and a separate disabled MT94 was queued for the same offline lab terminal. On a subsequent normal warm contact, a new immediate MT19 callback elicited MT9 and the terminal returned MT14 for the separately prepared disabled record. The cancellation batch completed independently, with the original data assignment and earlier enabled delivery history intact. This establishes native cancellation acknowledgement, not behavior during an interrupted transfer.

A second independently installed graphical variant, with a valid retained terminal clock, also acknowledged its own code-only MT94 after an immediate MT19/MT9 exchange. At the programmed time it made a separate dial to the receive-only observer. Actual modem answer and mark indications were observed, but the relay released before asserted carrier. The observer connection lasted approximately 3.85 seconds, with zero bytes received or sent; the saved flash remained byte-identical. The bounded observer later closed normally. This narrows the open gate to the modem carrier and application handshake, without establishing why the terminal released the line.

The second variant's separately queued disabled MT94 remains pending in the native bench. Its normal Craft refresh completed all eighteen assigned records, followed by the genuine MT9 callback return on the same connection. The host recorded the return but skipped the prepared cancellation because the earlier dialogue owned the per-connection download guard. A focused TCP regression verified the host correction. Native cancellation acknowledgement on this variant remains pending and must precede another code attempt.

| Editor | Encoding | Terminal acknowledgement | Device behavior |
| --- | --- | --- | --- |
| MT94 native schedule and selected-block fields, with separate pending and delivered cancellation. | 100-byte immutable record; selected target/revision, 2400 baud, exactly one selected 4 KiB block, and blank authentication verified in the acknowledged version. The separately disabled version retains the other parameters. | Actual warm graphical terminal returned MT14 for the original scheduled one-record batch after MT19/MT9. A separate warm MT19/MT9 contact acknowledged the disabled cancellation record. | Independent scheduled dial and TCP connection observed. Carrier assertion during that short call, application handshake, transfer, reboot and physical-device behavior remain unverified. Flash was unchanged and no application bytes were transmitted. |
