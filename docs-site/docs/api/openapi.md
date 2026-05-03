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

Requires **`DOTNET_ENVIRONMENT=Testing`** so startup does not connect to PostgreSQL during `swagger tofile` (uses in-memory DB + seed).

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

- Use **HTTPS** and **API keys** in production; see [Authentication](authentication.md) and [Security overview](../security/security-overview.md).
