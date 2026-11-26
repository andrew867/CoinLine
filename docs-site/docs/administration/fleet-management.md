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

Custom operator profiles may permit membership management separately from group administration. A membership-only operator selects a group from the membership lookup. Operators with Add can stage new memberships, and operators with Delete can stage removals. Read-only operators can inspect the list but cannot save changes.

## Routine call-ins

The phone detail page compares its assigned MT35 call-in parameters with the last acknowledged MT35, and identifies the most recent retained routine MT8 contact when the operator has session access. The repeat and retry intervals, call-record threshold and optional clock adjustment are displayed in the terminal clock domain. Assignment and acknowledgment are not interchangeable, and no predicted UTC contact time is presented.

Authorized operators can open the assigned native editor or make a separate draft for a phone-specific change without mutating an existing shared set. Publication and assignment or selected-record delivery are separate, explicitly reviewed actions.

## API

REST endpoints under `/api/customers`, `/api/sites`, `/api/terminals` — see [API overview](../api/overview.md) and OpenAPI.

## Related

- [Operator guide — Customers & sites](../operator-guide/customers-sites.md)
- [Operator guide — Terminals](../operator-guide/terminals.md)
