# Server deployment (CoinLine Server)

**CoinLine Server** is the API and persistence tier (ASP.NET Core, PostgreSQL, optional worker). Customers deploy it on **their own infrastructure** — VMs, Kubernetes, or bare metal behind a reverse proxy.

## Topology

- **CoinLine Server** listens for HTTPS (TLS terminates at your reverse proxy or load balancer).
- **PostgreSQL** holds fleet configuration, audit, rating, cards ledger (simulation-capable), firmware package metadata, and protocol diagnostics storage.
- **CoinLine Management Console** is static assets + SPA; host on CDN or the same origin as the API per your policy.

## Container deployment

From the `coinline/docker` directory:

```bash
docker compose up --build -d
```

Services:

- `postgres` — database (`coinline` database name in the sample compose file).
- `api` — CoinLine Server on port **5006** (adjust for production).

See [Docker Compose](../operations/docker-compose.md) for variables and profiles (including optional **worker** profile).

## Production checklist (abbreviated)

1. Set **`Security:Mode`** to **`ApiKey`** and configure **`COINLINE_API_KEYS`** (comma-separated) or `Security:ApiKeys`.
2. Disable demo seed: **`Platform:Seed:EnableDemoData`** = `false`.
3. Configure **CORS** origins for your Management Console URL.
4. Terminate **TLS** at nginx/Traefik/ALB; forward `X-Forwarded-*` as appropriate.
5. Restrict **`/metrics`** at the proxy if exposed.

Deep dive: [Production deployment](../operations/production-deployment.md), [Reverse proxy & TLS](../operations/reverse-proxy-tls.md), [PostgreSQL](../operations/postgresql.md).

## Related

- [Administrator — Fleet management](../administration/fleet-management.md)
- [Operations overview](../operations/overview.md)
