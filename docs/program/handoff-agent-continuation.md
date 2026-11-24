# CoinLine backend handoff

Follow the [Compose runbook](../../docker/README.md) to start PostgreSQL, the API, the console, and the worker. The API readiness endpoint is `http://localhost:5006/ready`. The console is available at `http://localhost:8080`, and the NCC listener accepts TCP on port `4000`. Start the emulator separately and point its NCC connection at the host listener.

Create a lab terminal whose five-byte `TerminalIdHex` matches the NCC address sent during INSTALL. The firmware tested in the lab used the configured line identifier in packed BCD. Assign a published table set with complete records for that firmware and use the console to check the terminal link and acknowledgements.

`NccModemListenService` accepts the socket. `NccLinkProcessor` binds and records terminal frames, handles clock sync and maintenance, sends table fragments, and serves rating requests. The download timeline completes each record when the terminal sends its type 14 acknowledgement. Rating records retain the raw request, response, and decision. Each install or rate session has its own correlation ID.

The local lab verified warm and cold downloads, an unavailable rate, a paid quote, and a toll-free call through the line simulator. See the [observed results](phase-5-gate-status.md) and [repeatable checks](phase-5-emulator-acceptance.md).
