# Background workers & hosted services

## API process (`HostPlatform.Api`)

| Hosted service | Type | Purpose |
|----------------|------|---------|
| **`RetentionTelemetryHostedService`** | `BackgroundService` | Loop **every 24h**; when **`Platform:Retention:TelemetryEnabled`** is true, logs a **count** of old DLOG rows with raw payload bytes — **never deletes** (destructive trim remains behind **`AllowDestructiveRawPayloadTrim`**) |

## Separate worker process (`HostPlatform.Worker`)

| Component | Type | Purpose |
|-----------|------|---------|
| **`Worker`** | `BackgroundService` | Every **60s**, upserts **`SubsystemHeartbeat`** with **`Subsystem = "worker"`** (`LastSeenUtc`) so readiness checks can observe a fresh worker pulse |

The worker host registers **only** EF Core + `Worker` — **no** migration or seed at startup (contrast API).

!!! note "Startup side effects (API only)"
    **`HostPlatform.Api`** `Program.cs` runs **`Database.MigrateAsync`** / **`EnsureCreatedAsync`** (Testing) **and** **`SeedData.EnsureSeedAsync`** at startup — not background workers but affects cold start.

## Readiness coupling

**`Platform:WorkerHeartbeat:RequireFreshHeartbeat`** gates **`/health/ready`** when worker stale — see [Observability](observability.md).
