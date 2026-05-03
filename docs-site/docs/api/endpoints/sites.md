# Sites API

**Route prefix:** `/api/sites`

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/sites` | List sites |
| POST | `/api/sites` | Create site |

!!! warning "Partial CRUD"
    No `GET /{id}` / `PUT` / `DELETE` in current controller — extend OpenAPI when added.

**Related UI:** `/sites`

**Entity:** `Site`

**Tests:** `ControllersApiTests.Sites_list_and_create`
