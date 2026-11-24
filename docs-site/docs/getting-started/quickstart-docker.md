# Quickstart (Docker)

From the repository root, run the lab stack.

```bash
cd docker
docker compose up -d --build
```

Compose starts PostgreSQL, the API, the console, and the worker. The API applies migrations at startup after PostgreSQL accepts TCP connections.

1. Check API readiness at `http://localhost:5006/ready` and console readiness at `http://localhost:8080/ready`.
2. Open `http://localhost:8080` and sign in with the seeded lab operator account described in the [Compose runbook](https://github.com/andrew867/CoinLine/blob/main/docker/README.md#checks).
3. For a firmware lab call, start the emulator separately and point it to TCP port `4000` on the host. See [Lab setup](../field-validation/lab-setup.md).

For host ports, persistent data, and the production overlay, see [Docker Compose](../operations/docker-compose.md).
