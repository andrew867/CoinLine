# Docker Compose

The base Compose file in `docker/` runs the lab stack. It publishes API HTTP on port `5006`, the web console on `8080`, and the NCC listener on TCP `4000`. PostgreSQL is available to containers as `postgres:5432`. The API handles NCC sessions and the worker writes its heartbeat.

Database and payload files are persisted in `docker/data/postgres` and `docker/data/payloads`. The API applies database migrations after the PostgreSQL TCP health check succeeds. Check the stack at `/ready` on either HTTP port.

The lab SIP audio modem uses UDP `5060` and RTP ports `40000` through `40007`. Copy `sip.env.example` to the ignored `sip.env` and fill the optional registration settings when a registrar is required. The optional serial ATA listener uses `SerialModem__Enabled` and `SerialModem__PortName`.

The production overlay sets `Production` and disables demo seeds. Provide `HOSTPLATFORM_API_KEYS` or `COINLINE_API_KEYS` for the API key gate. A nonempty `HOSTPLATFORM_API_KEYS` takes precedence. See [Production deployment](production-deployment.md).


For an isolated native acceptance listener that must never bind a LAN interface, set the API process environment variable `Ncc__LoopbackOnly=true`. This binds NCC to IPv4 loopback; an ordinary container's external published port will no longer reach that listener. Keep the normal deployment unchanged and use a dedicated test process or a deliberately isolated tunnel.
