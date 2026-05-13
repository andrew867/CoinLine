# Remaining work — ordered sequence

Single ordering for **baseline gaps** and **large themes** still open after Tranche 12. Execute top-down unless dependencies dictate otherwise.

1. **GAP-0011** — Branding / support surfaces (`docs-site/docs/reference/branding.md`): customer-visible names and support entry points finalized for external launch.
2. **GAP-0001** — Live **DLA / XMODEM** transport: UART-backed `IDlXmodemTransportAdapter` implementation (timing, ACK/NACK, OEM framing) — certification-gated.
3. **GAP-0007** — **Firmware job execution** on live modem path: orchestrator + worker integration once **GAP-0001** is certified; extends simulation steps already present.
4. **GAP-0002** — **Production operator auth**: OIDC and/or hardened API-key roles replacing dev headers (`DevOperatorMiddleware`) — authz integration tests.
5. **GAP-0010** — **Management Console** Maximizer-parity workflows: terminal/site/customer CRUD depth, table distribution UX, DLOG viewer polish, Playwright coverage per `docs/host_platform_greenfield/test-plans/web-admin-tests.md`.

**Cross-cutting:** deferred items in the [implementation gap register](./implementation-gap-register.md) § *Deferred / compatibility validation* (UART classification, live modem pacing, EEPROM layout proofs) stay visible via API notices until capture-backed certification.
