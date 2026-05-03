# Docker Compose (host platform)

- **API**: `http://localhost:5006` (maps container `5006`).
- **PostgreSQL**: not published to the host by default (avoids Windows reserved-port binding). The **api** service connects internally to **`postgres:5432`**. To expose Postgres locally, uncomment the `ports` mapping in `docker-compose.yml` (e.g. `127.0.0.1:55432:5432`).

```bash
cd host-platform/docker
docker compose build api
docker compose up -d
curl -sf http://localhost:5006/health
curl -sf http://localhost:5006/health/live
curl -sf http://localhost:5006/ready
curl -sf http://localhost:5006/swagger/v1/swagger.json | head
# Craft / field ops routes appear under `/api/craft/*`; operator console under `/api/operator/*` (see OpenAPI `paths`).
```

**Production-ish overlay** (API keys + demo seed off):  
`docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build`  
Set **`HOSTPLATFORM_API_KEYS`** (comma-separated) before starting — Production refuses empty keys.

If `curl` to `localhost:5006` fails with **connection refused**, confirm Docker Desktop published the port (`docker compose ps`), wait for the API container to finish migrating, and rule out a host firewall or reserved Windows port range blocking **5006**.

Migrations run on API startup (`MigrateAsync`). Command-line alternative from repo root:

`dotnet ef database update --project host-platform/backend/src/HostPlatform.Infrastructure/HostPlatform.Infrastructure.csproj --startup-project host-platform/backend/src/HostPlatform.Api/HostPlatform.Api.csproj`
