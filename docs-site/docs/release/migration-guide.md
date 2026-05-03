# Migration guide

## Database

1. Backup production.
2. Apply `dotnet ef database update` with new migration assembly.
3. Verify application health checks.

## Configuration

Compare `appsettings` keys release-to-release — see [environment variables](../reference/environment-variables.md).

## Application data

No automated customer data migration framework — plan bespoke scripts if schema reshapes legacy imports.
