# Environment variables & configuration keys

.NET maps environment variables to `appsettings` using **`__`** (double underscore) nested keys (e.g. `Platform__Seed__EnableDemoData`).

## Core

| Variable / key | Section | Purpose |
|----------------|---------|---------|
| `DOTNET_ENVIRONMENT` | runtime | `Production`, `Development`, `Testing` — controls DB provider and demo bootstrap, not authentication |
| `ConnectionStrings__Default` | ConnectionStrings | PostgreSQL connection string |
| `COINLINE_API_KEYS` | Security | Comma-separated API keys used when the legacy variable is empty |
| `HOSTPLATFORM_API_KEYS` | Security | Legacy name. A nonempty value takes precedence over `COINLINE_API_KEYS`. |

## NCC and modem inlets

| Variable / key | Purpose |
|----------------|---------|
| `Ncc__Port` | Listener port. Takes precedence over `COINLINE_NCC_PORT`; defaults to `4000` when neither supplies a valid port. |
| `COINLINE_NCC_LISTEN` | A value of `0` disables the listener. |
| `SipModem__Enabled` | Enables the SIP audio inlet. The base lab Compose stack sets this to `true`. |
| `SipModem__NccHost`, `SipModem__NccPort` | NCC destination for decoded modem bytes. |
| `SipModem__Registrar`, `SipModem__Username`, `SipModem__Password`, `SipModem__AssignedNumber` | Optional SIP registration settings. Store lab values in the ignored `docker/sip.env`. |
| `SerialModem__Enabled`, `SerialModem__PortName` | Enable the serial ATA inlet and identify its serial device. |

## Security (`Security`)

| Key | Default (appsettings) |
|-----|------------------------|
| `Security__Mode` | `Token`; `ApiKey` adds the deployment key requirement |
| `COINLINE_OPERATOR_TOKEN_KEY` | Required outside Development, random secret of at least 32 bytes |
| `COINLINE_DEV_OPERATOR_PASSWORD` | Optional Development demo account password; otherwise generated once |
| `COINLINE_BOOTSTRAP_ADMIN_USERNAME`, `COINLINE_BOOTSTRAP_ADMIN_PASSWORD` | One-time initial administrator credentials, remove after provisioning |
| `COINLINE_API_BIND_ADDRESS`, `COINLINE_WEB_BIND_ADDRESS` | Docker host bind addresses, default `127.0.0.1` |
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
