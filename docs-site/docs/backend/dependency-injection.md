# Dependency injection

Registered in `Program.cs`:

- `HostPlatformDbContext` — scoped
- `DlogTransactionEngine` — scoped
- `TableDistributionService` — scoped
- `FirmwareJobOrchestrator`, `IFirmwareExecutionPolicy` — scoped / singleton as configured
- `CapturedSessionReplayService` — scoped

Auth: `MinimumRoleAuthorizationHandler` + policies (`RequireOperator`, `RequireTechnician`, `RequireAdmin`).
