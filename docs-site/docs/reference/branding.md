# Branding and naming (CoinLine)

Use these names consistently in documentation, UI, API metadata, and support materials.

| Key | Value | Meaning | Where it appears |
|-----|-------|---------|------------------|
| `product_display_name` | CoinLine Payphone Management Platform | Full product name for customer-facing copy | `site_name` (MkDocs), marketing, contracts |
| `component_host` | CoinLine Host | On-terminal or field gateway services that speak host-side protocols (per integration) | Architecture, deployment discussions |
| `component_server` | CoinLine Server | .NET API, persistence, background services, OpenAPI | `Program.cs` OTel service `coinline-server`, ops runbooks |
| `component_console` | CoinLine Management Console | React operator web application | Browser title, sidebar, support tickets |
| `component_field_tools` | CoinLine Field Tools | Field validation, capture, and lab workflows (UI + API routes) | Field validation chapter |
| `component_api` | CoinLine API | Customer REST contract | OpenAPI title, integration guides |
| `product_slug` | `coinline` | Docker image namespace / package identifier (example) | `coinline/api` (example) |
| `env_prefix` | `COINLINE_` | Primary environment variable prefix for product-specific secrets and keys | `.env`, Kubernetes secrets |
| `docs_url` | `https://github.com/andrew867/CoinLine/tree/main/docs-site/docs` | MkDocs documentation source on GitHub | README, support |
| `public_repo_url` | `https://github.com/andrew867/CoinLine` | Public source / issue tracker URL | `mkdocs.yml` `repo_url`, footers |
| `copyright` | Copyright © 2026 Andrew Green | Default legal line | License, footers |
| `placeholder_support_email` | _TBD_ | Customer support contact | Replace before external launch |
| `placeholder_support_portal` | _TBD_ | Support portal URL | Replace before external launch |

## Replacement instructions

1. When forking or white-labeling, replace `product_display_name` and `product_slug` first, then search for `coinline` and `CoinLine` in `mkdocs.yml`, `package.json`, and OpenAPI document filter.
2. Set `env_prefix` keys in production secret stores; never commit real API keys.
3. Update `docs_url` and `public_repo_url` if the public repository location changes.
4. Fill `placeholder_support_*` before customer handoff.

## Release checklist item

- [ ] All branding keys in this table reviewed; no internal codenames in customer PDFs or public site; `COINLINE_` keys documented in `.env.example`.
