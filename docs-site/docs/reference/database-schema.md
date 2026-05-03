# Database schema

Source of truth: **EF Core model** in `HostPlatformDbContext` + migrations under `Persistence/Migrations/`.

Key table names (PostgreSQL, snake_case not used by default — EF uses relational names as configured): see snapshot `HostPlatformDbContextModelSnapshot.cs`.

Highlights: `DlogTransactions`, `NccFrameCaptures`, `CapturedHardwareSessions`, `TablePayloads`, `FirmwareUpdateJobs`, `AuditEvents`.
