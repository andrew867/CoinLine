# CoinLine lab (Docker Compose)

From this directory:

```bash
docker compose up -d --build
```

That starts PostgreSQL, the API, the management console, and the worker.

| Service | Address | Role |
| --- | --- | --- |
| api | http://localhost:5006 | HTTP API. Swagger UI at `/swagger`. Liveness is `/health`. |
| api | `localhost:4000` | NCC listener (`COINLINE_NCC_PORT=4000`). The listener stays enabled. |
| api | `localhost:5060` UDP | SIP audio modem. G.711 only. Tones are Bell 202 at 1200 baud, the same pair `minimodem 1200` uses. Decoded bytes go to the listener on port 4000. RTP is UDP 40000–40007. Optional REGISTER: copy `sip.env.example` to `sip.env` and set the SIP credential, public IP, and assigned number. Platform status can run a loopback self-test. |
| api | serial (optional) | Set `SerialModem__Enabled=true` and `SerialModem__PortName` to answer RING with ATA and bridge bytes to port 4000. Use a device path visible to the API process. A Linux container requires the device to be passed through. Off by default. |
| web | http://localhost:8080 | Management console. `/api`, `/health`, `/health/live`, `/ready`, and `/swagger` are proxied to the API. |
| postgres | `postgres:5432` on the Compose network | Not published on the host. |
| worker | no host port | Writes the worker heartbeat row only. It does not handle sessions. |

The base Compose file runs the API in Development and loads demo data. A real operator token is still required. The API and console are published on loopback by default; opt in to a different host address with `COINLINE_API_BIND_ADDRESS` or `COINLINE_WEB_BIND_ADDRESS`. Migrations run when the API starts.

## Durable data

`data/postgres` and `data/payloads` are bind mounts. See [data/README.md](data/README.md). Do not commit their contents.

`data/payloads` is mounted at `/var/coinline/payloads` and set as `Platform__PayloadStorage__RootPath`.

## Emulator

Start the emulator on the host. Compose does not build or run it. Point it at TCP port 4000. The NCC listener is published on loopback by default. Set `COINLINE_NCC_BIND_ADDRESS` only if a secured LAN or gateway needs access. The default concurrent session limit is 32, receive timeout is 60 seconds, and idle timeout is 120 seconds without a valid frame. Adjust `COINLINE_NCC_MAX_CONNECTIONS`, `COINLINE_NCC_READ_TIMEOUT_SECONDS` and `COINLINE_NCC_IDLE_TIMEOUT_SECONDS` when required; the INSTALL acknowledgement deadline remains separate.

## Production overlay

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

Set `COINLINE_OPERATOR_TOKEN_KEY` to a random secret of at least 32 bytes and `HOSTPLATFORM_API_KEYS` or `COINLINE_API_KEYS` (comma-separated) before starting. When both API key variables are set, `HOSTPLATFORM_API_KEYS` is used. For a fresh database, set `COINLINE_BOOTSTRAP_ADMIN_USERNAME` and `COINLINE_BOOTSTRAP_ADMIN_PASSWORD` (at least 12 characters) to create the first named administrator, then remove the bootstrap variables. Production refuses to start with a missing or invalid signing key, or with an empty API key list in ApiKey mode. If upgrading a database with the retired demo account, provision a distinct replacement administrator before startup; the old default account is disabled.

## Checks

```bash
curl -sf http://localhost:5006/health
curl -sf http://localhost:8080/
curl -sf http://localhost:8080/health
curl -sf http://localhost:8080/ready
```

A TCP connection to `localhost:4000` should be accepted once the API is up. PostgreSQL files appear under `data/postgres` after the database initializes.

In Development, the demo account `operator` is created on first start with `COINLINE_DEV_OPERATOR_PASSWORD` (at least 12 characters), or a random password shown once in the API startup output. Sign in at http://localhost:8080/login with the configured or generated credentials. A persistent signing key is recommended for a lab that retains its database. Additional people are added under People. Alarms and cash reports from a phone appear under Alarms.

Postgres is healthy only when `pg_isready` reaches `127.0.0.1`. The image's first server listens on a local socket and then restarts before it accepts TCP, so a socket-only check lets the API start too early.

If `localhost:5006` refuses connections, check `docker compose ps`, wait for the API to finish migrating, and confirm nothing else has reserved that port.
