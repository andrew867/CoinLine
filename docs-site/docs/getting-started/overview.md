# Overview

The **CoinLine Payphone Management Platform** is an enterprise service and operator UI for managing terminals, configuration tables, DLOG records, rating experiments, card ledger scaffolding, craft sessions, and firmware job metadata.

!!! info "Scope"
    Deployments should validate behavior against **supported terminal firmware versions** and **supported payphone models** listed in your OEM compatibility pack — earlier generations may share framing discipline but require **device compatibility** confirmation in your lab.

## Capabilities (truthful)

| Area | Status |
|------|--------|
| NCC framing decode (strict + diagnostic) | `IMPLEMENTED` — see protocol libraries |
| DLOG ingest + unknown MT retention | `IMPLEMENTED` |
| Table opaque bytes + SHA-256 | `IMPLEMENTED` |
| Rating quote / authorize (MVP) | `IMPLEMENTED` — not full production tariff parity without customer UAT |
| Cards ledger API | `IMPLEMENTED` — **`SIMULATION ONLY`** defaults until your PCI program enables live scope |
| Craft command simulation | `IMPLEMENTED` — live transport requires **field validation** with supported hardware |
| Firmware registry + jobs | `IMPLEMENTED` — live programming paths are **gated** / simulation-first |

## First links

- [Quickstart (Docker)](quickstart-docker.md)
- [Quickstart (dev)](quickstart-dev.md)
- [Architecture at a glance](architecture-at-a-glance.md)
