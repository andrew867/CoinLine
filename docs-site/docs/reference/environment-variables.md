# Environment variables & configuration keys

.NET maps environment variables to `appsettings` using **`__`** (double underscore) nested keys (e.g. `Platform__Seed__EnableDemoData`).

## Core

| Variable / key | Section | Purpose |
|----------------|---------|---------|
| `DOTNET_ENVIRONMENT` | runtime | `Production`, `Development`, `Testing` — affects auth bypass and DB provider |
| `ConnectionStrings__Default` | ConnectionStrings | PostgreSQL connection string |
| `COINLINE_API_KEYS` | Security | **Preferred** — comma-separated API keys (merged into `Security:ApiKeys` in `Program.cs`) |
| `HOSTPLATFORM_API_KEYS` | Security | Legacy alias — same behavior as `COINLINE_API_KEYS` |

## Security (`Security`)

| Key | Default (appsettings) |
|-----|------------------------|
| `Security__Mode` | `Development` — use **`ApiKey`** in production |
| `Security__ApiKeys` | `[]` — prefer env |
| `Security__GlobalRequestsPerMinutePerIp` | `0` = disabled |

## Swagger (`Swagger`)

| Key | Purpose |
|-----|---------|
| `Swagger__Enabled` | Enable Swagger UI / OpenAPI emission at runtime |

## Observability (`Observability`)

| Key | Purpose |
|-----|---------|
| `Observability__MetricsEnabled` | Prometheus exporter |
| `Observability__MetricsPath` | Default `/metrics` |

## Platform (`Platform`)

| Key | Purpose |
|-----|---------|
| `Platform__Seed__EnableDemoData` | Demo customer/terminal seed |
| `Platform__PayloadStorage__RootPath` | Optional filesystem blob root |
| `Platform__PayloadStorage__RequireHealthyWhenConfigured` | Readiness fails if path bad |
| `Platform__QueryLimits__MaxPageSize` | Pagination cap |
| `Platform__QueryLimits__DefaultAuditPageSize` | Audit default |
| `Platform__QueryLimits__MaxAuditUnpagedTake` | Audit safety |
| `Platform__WorkerHeartbeat__RequireFreshHeartbeat` | Ready gate |
| `Platform__WorkerHeartbeat__MaxStaleSeconds` | Staleness threshold |
| `Platform__Retention__TelemetryEnabled` | Retention telemetry hosted service logs counts |
| `Platform__Retention__AllowDestructiveRawPayloadTrim` | **Dangerous** — default false |
| `Platform__Retention__RawPayloadOlderThanDays` | Eligibility age for telemetry |
| `Platform__Cors__AllowedOrigins` | Browser CORS origins |

## Job orchestration (`JobOrchestration`)

| Key | Purpose |
|-----|---------|
| `JobOrchestration__MaxTransientRetries` | DB retry for firmware orchestrator |
| `JobOrchestration__TransientRetryDelayMs` | Backoff |

## Card payments (`CardPayments`)

| Key | Purpose |
|-----|---------|
| `CardPayments__SimulationMode` | **`SIMULATION ONLY`** banner default true |
| `CardPayments__PhysicalCardWritesDisabled` | Refuse live writes default true |

## Firmware (`Firmware`)

| Key | Purpose |
|-----|---------|
| `Firmware__AllowLiveFlashing` | Default **false** — **PRODUCTION GUARD REQUIRED** |

Canonical prose: `coinline/docs/configuration/secrets-and-config.md` (repository).
