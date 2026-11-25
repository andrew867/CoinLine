# Table definitions & versions API

**Route prefix:** `/api/tables`

Definitions & versions:

| Method | Path |
|--------|------|
| GET/POST | `/api/tables/definitions` |
| GET | `/api/tables/definitions/{id}` |
| GET | `/api/tables/reference-records` |
| GET/POST | `/api/tables/versions` |

The reference endpoint joins the native editor catalogue, legacy family configuration references, known firmware record lengths, retained emulator acknowledgement observations and narrowly described behavior milestones with the registered definition list. It does not create definitions, alter portable payloads or infer physical behavior. Operational messages and code-download scheduling records are classified separately from ordinary configuration records.

**UI:** `/table-definitions`, `/table-definitions/:id`, `/table-versions`

**Entities:** `TableDefinition`, `TableVersion`, `TablePayload`
