# Cards

CoinLine separates terminal card configuration, card accounts and runtime authorization. A phone may accept a card in its INSTALL or Craft reader test without having an authorization provider connected.

- **Table sets:** edit card ranges, carrier acceptance, stored-value issuer settings and the applicable terminal features on an appropriate firmware target. Published sets remain read-only.
- **Card accounts:** maintain products, token references, classifications and the host balance ledger. Reconciliation is a separate operator workflow; the account catalog does not authorize a live card call.
- **Runtime calls:** the terminal can send MT1 or MT2 after rating. CoinLine acknowledges the message and returns the corresponding MT3 or MT65 response without starting a table download.

## Authorization provider integration

The **Card accounts** page reports whether a runtime authorization provider is registered. Registration is not a live connectivity or settlement check. The built-in host has no live authorization provider. It declines runtime card requests even if a matching card product exists in the operator catalog. An installed, trusted in-process provider can supply a decision through the `INccCardAuthorizationProvider` boundary. The host validates the response shape, declines when the provider is unavailable or reports an invalid decision, and records a redacted outcome rather than cardholder data. Retries within the same NCC dialogue reuse the original response without authorizing a second time.

The integration fixture supplies an opaque synthetic request to a test-only provider, exercising both authorization formats, provider decline, invalid response, exception and retransmission. No test provider is registered in the ordinary application. The tests do not certify payment processing, settlement, real card acceptance or a firmware payment transaction. A production provider must implement its own secure credential handling, authorization and durable idempotency across reconnects; processor settlement remains a separate integration task.

Review the existing host-ledger and reconciliation workflows under [Cards and accounts](../administration/cards-and-accounts.md). Physical card writes are not enabled by this integration.
