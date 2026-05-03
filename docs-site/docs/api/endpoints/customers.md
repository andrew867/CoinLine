# Customers API

**Route prefix:** `/api/customers`

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| GET | `/api/customers` | Operator dev / ApiKey | List customers |
| POST | `/api/customers` | Operator dev / ApiKey | Create customer |
| GET | `/api/customers/{id}` | Operator dev / ApiKey | Get customer |
| PUT | `/api/customers/{id}` | Operator dev / ApiKey | Update customer |

!!! note "Audit"
    Create/update writes `customers` audit rows — detail JSON may include HARDWARE_VALIDATION_REQUIRED reminders.

**Related UI:** `/customers`, `/customers/:id`

**Entities:** `Customer`

**Tests:** `ControllersApiTests.Customers_crud_smoke`

```bash
curl -sS http://localhost:5006/api/customers
```

See OpenAPI paths `/api/customers`.
