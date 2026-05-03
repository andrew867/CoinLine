# Payphone validation with CoinLine Host

Validate a **Millennium payphone** against **CoinLine Server** using controlled lab steps before fleet rollout.

## Scope

Validation confirms **device compatibility** for:

- Registration and status reporting.
- **Table download** and assignment alignment.
- **DLOG** or upload paths used in your deployment.
- **Rating** and **card** flows if in scope (often simulation-first).

## Recommended sequence

1. **Lab setup** — [Lab setup](lab-setup.md), modem/line characteristics documented.
2. **Terminal registration** — Create customer/site/terminal records; align transport endpoints with your network.
3. **Table download** — [Table download](table-download.md); capture host and terminal evidence.
4. **Upload / DLOG** — [Upload capture](upload-capture.md) as applicable.
5. **Rated call** — [Rated call](rated-call.md) if rating is in scope.
6. **Evidence capture** — [Evidence capture](evidence-capture.md); archive traces under your change control.

Use **CoinLine Field Tools** (Management Console pages for captures, hardware validation API where deployed) to align evidence with support cases.

## Related

- [Hardware validation API](../api/endpoints/hardware-validation.md)
- [Compatibility validation items](../reference/compatibility-validation-items.md)
