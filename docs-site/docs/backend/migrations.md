# Migrations

```bash
cd backend
dotnet ef migrations add <Name> --project src/HostPlatform.Infrastructure --startup-project src/HostPlatform.Api
dotnet ef database update --project src/HostPlatform.Infrastructure --startup-project src/HostPlatform.Api
```

Production applies migrations at API startup when using Npgsql (`Program.cs`).
