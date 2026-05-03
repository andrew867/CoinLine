# CoinLine Payphone Management Platform

**CoinLine** is an OEM-grade, server-hosted management platform for **Millennium payphone fleets**. Customers deploy **CoinLine Server** and **CoinLine Management Console** on their own infrastructure to operate provisioning, table distribution, call rating, card and account administration, technician craft workflows, operational audit trails, and protocol-aware diagnostics for supported terminals.

**Product components:** [CoinLine Host](reference/branding.md) (runtime services on each payphone or field gateway, as defined in your integration) · **CoinLine Server** (API and data plane) · **CoinLine Management Console** (web operator UI) · **CoinLine Field Tools** (field validation and capture workflows) · **CoinLine API** (REST integration surface).

!!! warning "Field and device validation"
    Protocol behavior must be confirmed against **supported terminal hardware** and your network environment. See [Field validation](field-validation/overview.md) and [Known limitations](reference/known-limitations.md).

## Documentation map

| Audience | Start here |
|----------|------------|
| Operators & administrators | [Administration](administration/fleet-management.md), [Operator guide](operator-guide/overview.md) |
| Installers & platform owners | [Server deployment](deployment/server-deployment.md), [Operations](operations/overview.md) |
| Integrators & developers | [CoinLine API](api/openapi.md), [Security overview](security/security-overview.md) |
| Field engineers | [Payphone validation](field-validation/payphone-validation.md), [Field validation](field-validation/overview.md) |

## Repository

MkDocs documentation source in this repository: [`docs-site/docs/`](https://github.com/andrew867/CoinLine/tree/main/docs-site/docs)  
Public repository: **[github.com/andrew867/CoinLine](https://github.com/andrew867/CoinLine)**

## Status

See [Changelog](release/changelog.md) and [Known limitations](reference/known-limitations.md) for **v0.1.0**. This is **not** a statement of complete certification for all payphone models or regulatory regimes.

---

**Copyright © 2026 Andrew Green** (see [License](release/license.md)).
