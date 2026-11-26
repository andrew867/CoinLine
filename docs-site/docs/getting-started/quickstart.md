# Quickstart

Run **CoinLine Payphone Management Platform** locally for evaluation: **CoinLine Server** (API) + **PostgreSQL** + **CoinLine Management Console** (web UI).

## Prerequisites

- .NET SDK 9+
- Node.js 20+
- PostgreSQL 16+ (local install or Docker)

## 1. Database

Using Docker:

```bash
docker run --name coinline-pg -e POSTGRES_PASSWORD=coinline -e POSTGRES_USER=coinline \
  -e POSTGRES_DB=coinline -p 5432:5432 -d postgres:16-alpine
```

## 2. CoinLine Server (API)

From the `coinline` package root:

```bash
cd backend
dotnet tool restore
dotnet run --project src/HostPlatform.Api --launch-profile http
```

- API: `http://localhost:5006`; migrations run on startup.
- In Development, configure `COINLINE_DEV_OPERATOR_PASSWORD` or use the random password shown once in startup output. Sign in as `operator` in the console.
- All protected API calls, including Swagger, require an operator token. Outside Development, configure `COINLINE_OPERATOR_TOKEN_KEY` and an initial administrator; see [Authentication](../api/authentication.md).

## 3. CoinLine Management Console

```bash
cd web
npm install
npm run dev
```

- Console: `http://localhost:5173` (proxies `/api` to the API)

## 4. Docker Compose (alternative)

From `coinline/docker`:

```bash
docker compose up --build
```

Adjust ports and secrets per [Server deployment](../deployment/server-deployment.md).

## Next steps

- [Fleet management](../administration/fleet-management.md)
- [Environment variables](../reference/environment-variables.md)
- [Security overview](../security/security-overview.md)
