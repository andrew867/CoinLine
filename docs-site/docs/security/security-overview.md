# Security overview (CoinLine)

**CoinLine Payphone Management Platform** is designed for **customer-operated** infrastructure. Security ownership is **shared**: CoinLine provides mechanisms; customers configure secrets, network isolation, and compliance scope.

## Authentication

- Every protected API route requires a validated operator token, including in Development and Testing.
- API keys are an optional additional deployment gate, not a substitute for an operator account.
- Set a unique signing secret outside Development. No default production administrator is created. The API binds to loopback by default.

Use **`COINLINE_API_KEYS`** (comma-separated) or configuration equivalents — see [Environment variables](../reference/environment-variables.md).

## Transport

- Terminate **TLS** at your reverse proxy or ingress.
- Do not expose the API anonymously to the public internet.

## Audit

Sensitive mutations write **audit events** (categories vary by domain). Review [Audit model](audit-model.md) and [Dangerous operations](dangerous-operations.md).

## Payment-adjacent data

Card read/write observations retain only an optional last-four fragment, card type, outcome and related record identifiers. Legacy opaque input fields are ignored. Manual transaction amounts and reconciliation are retained without caller-provided raw payload or arbitrary detail. The migration discards historical opaque event data and preserves separately evidenced settlement records. Never send full PAN, track or CVV; use only authorized token references.

## Related

- [Threat model](threat-model.md)
- [Secrets](secrets.md)
- [Hardening checklist](hardening-checklist.md)
