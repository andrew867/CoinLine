# Call rating administration

**CoinLine Server** provides **call rating** workflows: rate plans, rules, destination prefixes, time bands, tariffs, number classes, quote/authorize APIs, and **call records** storage.

## Configuration order

1. Define **rate plans** and versions under the owning customer context.
2. Add **rules**, **prefixes**, **tariffs**, and **time bands** as required by your tariff model.
3. Publish plan versions when ready (confirm-guarded).
4. Use **rating quote** and **authorize** APIs or Management Console tools for validation.

## Validation

Set/table-rated modes and unknown prefixes may surface **compatibility diagnostics** until validated on **supported terminal hardware** — see [Known limitations](../reference/known-limitations.md) and [Field validation — Rated call](../field-validation/rated-call.md).

## Related

- [Operator — Rating](../operator-guide/rating.md)
- [Protocols — Rating & call flow](../protocols/rating-call-flow.md)
