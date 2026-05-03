# Security overview (CoinLine)

**CoinLine Payphone Management Platform** is designed for **customer-operated** infrastructure. Security ownership is **shared**: CoinLine provides mechanisms; customers configure secrets, network isolation, and compliance scope.

## Authentication

- **API keys** (HTTP headers) for automation and Management Console traffic — protect like passwords; rotate on schedule.
- **Development** auth bypass must be **disabled** in production (`Security:Mode`).

Use **`COINLINE_API_KEYS`** (comma-separated) or configuration equivalents — see [Environment variables](../reference/environment-variables.md).

## Transport

- Terminate **TLS** at your reverse proxy or ingress.
- Do not expose the API anonymously to the public internet.

## Audit

Sensitive mutations write **audit events** (categories vary by domain). Review [Audit model](audit-model.md) and [Dangerous operations](dangerous-operations.md).

## Payment-adjacent data

Card APIs operate on **tokens**, masked fragments, and opaque payloads — **never** persist full track, PAN, or CVV through these endpoints in production without an approved PCI architecture.

## Related

- [Threat model](threat-model.md)
- [Secrets](secrets.md)
- [Hardening checklist](hardening-checklist.md)
