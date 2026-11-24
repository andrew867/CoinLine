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

The base Compose file runs the API in Development so the console's operator headers are accepted and demo seed data loads. Migrations run when the API starts.

## Durable data

`data/postgres` and `data/payloads` are bind mounts. See [data/README.md](data/README.md). Do not commit their contents.

`data/payloads` is mounted at `/var/coinline/payloads` and set as `Platform__PayloadStorage__RootPath`.

## Emulator

Start the emulator on the host. Compose does not build or run it. Point it at TCP port 4000.

## Production overlay

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

Set `HOSTPLATFORM_API_KEYS` or `COINLINE_API_KEYS` (comma-separated) before starting. When both are set, `HOSTPLATFORM_API_KEYS` is used. Production refuses to start in ApiKey mode when that list is empty.

## Checks

```bash
curl -sf http://localhost:5006/health
curl -sf http://localhost:8080/
curl -sf http://localhost:8080/health
curl -sf http://localhost:8080/ready
```

A TCP connection to `localhost:4000` should be accepted once the API is up. PostgreSQL files appear under `data/postgres` after the database initializes.

Sign in at http://localhost:8080/login as `operator` with password `lab-operator`. That account is created on first start when it does not already exist. Additional people are added under People. Alarms and cash reports from a phone appear under Alarms.

Postgres is healthy only when `pg_isready` reaches `127.0.0.1`. The image's first server listens on a local socket and then restarts before it accepts TCP, so a socket-only check lets the API start too early.

If `localhost:5006` refuses connections, check `docker compose ps`, wait for the API to finish migrating, and confirm nothing else has reserved that port.
