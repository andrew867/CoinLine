# Database

- **Provider:** PostgreSQL (Npgsql) in production; **InMemory** for `Testing`.
- **Context:** `HostPlatformDbContext` — see [Entity reference](../reference/entity-reference.md).
- **Concurrency:** optimistic `Version` on `AuditableEntity` where applicable.
