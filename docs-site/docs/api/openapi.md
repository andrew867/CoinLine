# CoinLine API — OpenAPI specification

The **CoinLine API** is described by OpenAPI 3.x artifacts committed with the product package and by **live Swagger** at `/swagger` when **CoinLine Server** runs.

## Static artifacts

| File | Format |
|------|--------|
| [coinline.openapi.json](openapi/coinline.openapi.json) | OpenAPI 3.0 JSON |
| [coinline.openapi.yaml](openapi/coinline.openapi.yaml) | YAML export |

## Regenerate

From the **repository root**:

```bash
bash docs-site/scripts/export-openapi.sh
```

The export scripts use `DOTNET_ENVIRONMENT=Testing`, an ephemeral signing key and disabled modem listeners, so startup uses an isolated in-memory database instead of PostgreSQL.

Windows:

```powershell
docs-site\scripts\export-openapi.ps1
```

## Local Swagger UI

```bash
cd backend
dotnet run --project src/HostPlatform.Api --launch-profile http
```

Open **http://localhost:5006/swagger**

!!! warning "Known gaps vs runtime"
    **`swagger tofile`** reflects controller-discovered routes. **Minimal** endpoints (health, readiness, optional metrics) may be **partially absent** from the committed JSON — validate probes against running Swagger or your integration tests.

!!! tip "XML comments"
    The API project generates XML documentation (`GenerateDocumentationFile`) for richer OpenAPI when Swashbuckle includes it.

## Customer integration

- Use **HTTPS** and **operator tokens** in production, plus the deployment API key when enabled; see [Authentication](authentication.md) and [Security overview](../security/security-overview.md).
