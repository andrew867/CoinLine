# Native code-server observation

The MT94 Manager scheduling workflow prepares, delivers and records the acknowledgement of code-download parameters. The subsequent independent code-server call is a different protocol conversation. Parameter acknowledgement does not prove the call occurred, an image was transferred, or the terminal rebooted into the requested version.

For an isolated laboratory phone whose normal INSTALL and restart have been verified, the receive-only observer provides a first test of the independent call route. It listens on IPv4 loopback only, handles one connection at a time and stops after its configured lifetime. Each connection has independent idle, total-time and received-byte limits. It never sends an image, a greeting or any application data.

```sh
python3 tools/code_server_observer.py --port 4001 \
  --duration-seconds 900 --session-seconds 60 --idle-seconds 15 \
  --output code-server-observation.jsonl
```

Use a distinct lab-only code-server modem alias routed to this listener. Keep ordinary NCC access and the phone's existing data assignment separate. The output file must be new. Its JSON lines record connection events, UTC timestamps, receive counts and possible standalone control octets. No arbitrary payload or peer identity is retained. A TCP receive boundary is not necessarily a protocol frame; a possible control classification alone is not proof of a specific protocol. Keep the report local to the bench.

The next acceptance gates are an observed native handshake, documented compatible transfer framing, an independently approved target image and isolated code-download parameters. Actual image delivery, device verification, reboot and interrupted-transfer recovery are not established by this observer. Do not direct an operator's in-service phone at the bench endpoint.

The tests use only loopback synthetic data. They check that the listener sends zero bytes, that arbitrary input is absent from its report, that idle connections close and that the receive cap applies.
