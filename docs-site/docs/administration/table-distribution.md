# Table distribution

**CoinLine Server** distributes configuration **tables** to payphones using versioned **table definitions**, **payloads**, and **table sets**. Operators publish sets and queue **downloads** to terminals.

## Concepts

| Concept | Role |
|---------|------|
| Table definition | Logical table identity (name, number) |
| Table payload | Opaque bytes + checksum for a given definition revision |
| Table set | Bundle of definitions at specific versions |
| Download batch | Host-side intent to push a set to a terminal |

Raw table bytes remain **opaque** at the API boundary; interpretation follows **terminal firmware compatibility** for supported models.

## Operator steps

1. Create or update **table definitions** and upload payloads (Management Console or API).
2. Build a **table set** referencing definition versions.
3. **Publish** when ready (confirm-guarded operations — see UI prompts).
4. Create **terminal assignments** linking a terminal to the desired set.
5. Queue **downloads** and monitor batch status.

Destructive or disruptive actions require explicit **confirmation** query parameters and produce **audit** events.

## Related

- [Operator — Table management](../operator-guide/table-management.md)
- [Operator — Downloads](../operator-guide/downloads.md)
- [Protocols — Table distribution](../protocols/table-distribution.md)
