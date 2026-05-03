# Fleet management

Use **CoinLine Management Console** (and equivalent **CoinLine API** calls) to manage:

- **Customers** — billing / organizational boundary for terminals.
- **Sites** — physical locations within a customer.
- **Terminal groups** — optional grouping for rollout and reporting.
- **Terminals** — identity, transport endpoints, firmware compatibility references, table assignments.

## Hierarchy

```text
Customer
  └── Site
        └── Terminal(s)
```

Assignments for **table sets** can apply at customer, site, or terminal scope depending on product configuration — see [Table distribution](table-distribution.md).

## Console workflow

1. Create or import a **customer** record.
2. Add **sites** under that customer.
3. Register **terminals** with identifiers and transport settings appropriate to your deployment.
4. Assign **table sets** and schedule **downloads** per [Table distribution](table-distribution.md).

## API

REST endpoints under `/api/customers`, `/api/sites`, `/api/terminals` — see [API overview](../api/overview.md) and OpenAPI.

## Related

- [Operator guide — Customers & sites](../operator-guide/customers-sites.md)
- [Operator guide — Terminals](../operator-guide/terminals.md)
