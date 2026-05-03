# Architecture at a glance

```mermaid
flowchart LR
  subgraph Clients
    UI[React operator UI]
    TT[Terminal / modem]
  end
  subgraph Host["Host platform"]
    API[ASP.NET Core API]
    DB[(PostgreSQL)]
    W[Worker optional]
  end
  UI -->|HTTPS /api| API
  TT -.->|NCC/DLOG transport| API
  API --> DB
  W --> DB
```

## Layers

- **HostPlatform.Api** — HTTP, auth middleware, controllers.
- **Domain** — entities and enums.
- **Infrastructure** — EF Core, DLOG engine, table distribution.
- **Protocols.\*** — NCC, DLOG pure parsing/classification.
- **Rating / Cards / Firmware** — domain services & policies.

See [Architecture → Service boundaries](../architecture/service-boundaries.md).
