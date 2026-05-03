# Backend overview

- **Framework:** ASP.NET Core 9, EF Core 9, Npgsql.
- **Entry:** `HostPlatform.Api` → `Program.cs` (CORS, rate limit, API key gate, dev operator, Swagger, health, OpenTelemetry optional).
- **Testing:** in-memory DB in `Testing` environment; integration tests use `WebApplicationFactory`.

See [Solution structure](solution-structure.md) and [Configuration](configuration.md).

## Extending the backend

| Task | Where |
|------|-------|
| New REST endpoint | Add action on controller under `HostPlatform.Api/Controllers/`; register nothing extra if using conventional routing. |
| New protocol parser | Prefer `HostPlatform.Protocols.*`; wire via Infrastructure or Api service — keep bytes immutable. |
| New DLOG MT | Seed registry data + document in `docs/protocols/host_platform/dlog_tables/` — classifier stays non-authoritative for layout. |
| New table definition | Create `TableDefinition` + versions via API; payload stays opaque bytes. |
