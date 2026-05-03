# Quickstart (development)

## Prerequisites

- .NET SDK **9+**
- Node.js **20+** / npm
- PostgreSQL **16+** (local or container)

## Database

```bash
docker run --name hostplatform-pg \
  -e POSTGRES_PASSWORD=host -e POSTGRES_USER=host -e POSTGRES_DB=hostplatform \
  -p 5432:5432 -d postgres:16-alpine
```

## Backend

```bash
cd backend
dotnet tool restore
dotnet ef database update --project src/HostPlatform.Infrastructure --startup-project src/HostPlatform.Api
dotnet run --project src/HostPlatform.Api --launch-profile http
```

API: **http://localhost:5006** · Swagger: **http://localhost:5006/swagger**

## Frontend

```bash
cd web
npm install
npm run dev
```

UI: **http://localhost:5173** (proxies `/api` → API).

## Seed data

Controlled by `Platform:Seed:EnableDemoData` (see [Seed data](../backend/seed-data.md)).

## First workflow

[Operator guide → Common workflows](../operator-guide/common-workflows.md)
