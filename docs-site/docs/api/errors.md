# Errors

Typical patterns:

- **400** — validation (`confirm` missing, bad hex, invalid enum).
- **401/403** — auth / policy failure.
- **404** — unknown id.
- **409** — conflict (simulation gate, incompatible firmware rule).

Prefer JSON body with `{ "error": "..." }` — exact shapes vary by endpoint; [**OpenAPI**](openapi/coinline.openapi.json) lists response schemas where emitted.
