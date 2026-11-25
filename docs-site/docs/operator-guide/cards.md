# Cards

CoinLine separates terminal card configuration, card accounts and runtime authorization. A phone may accept a card in its INSTALL or Craft reader test without having an authorization provider connected.

- **Table sets:** edit card ranges, carrier acceptance, stored-value issuer settings and the applicable terminal features on an appropriate firmware target. Published sets remain read-only.
- **Card accounts:** maintain products, token references, classifications and the host balance ledger. Reconciliation is a separate operator workflow; the account catalog does not authorize a live card call.
- **Runtime calls:** the terminal can send MT1 or MT2 after rating. CoinLine acknowledges the message and returns the corresponding MT3 or MT65 response without starting a table download.

## Authorization provider integration

The **Card accounts** page reports whether a runtime authorization provider is registered. Registration is not a live connectivity or settlement check. The built-in host has no live authorization provider. It declines runtime card requests even if a matching card product exists in the operator catalog. An installed, trusted in-process provider can supply a decision through the `INccCardAuthorizationProvider` boundary. The host validates the response shape, declines when the provider is unavailable or reports an invalid decision, and records a redacted outcome rather than cardholder data. Retries within the same NCC dialogue reuse the original response without authorizing a second time.

The integration fixture supplies an opaque synthetic request to a test-only provider, exercising both authorization formats, provider decline, invalid response, exception and retransmission. No test provider is registered in the ordinary application. The tests do not certify payment processing, settlement, real card acceptance or a firmware payment transaction. A production provider must implement its own secure credential handling, authorization and durable idempotency across reconnects; processor settlement remains a separate integration task.

Review the existing host-ledger and reconciliation workflows under [Cards and accounts](../administration/cards-and-accounts.md). Physical card writes are not enabled by this integration.

## Isolated synthetic terminal acceptance

The integration test assembly contains an opt-in, manual emulator harness. It runs a loopback-only NCC listener in the Testing environment with its own in-memory database, a synthetic terminal, a test account, and an exact reserved-destination five-cent-per-minute quote. The shared application, database and telephone routes are not used. The fixture recognizes only its designated nonproduction card, terminal and reserved test destination. It never forwards to a real issuer and never writes a physical card.

Create a private settings file outside the tracked source tree with the following fields:

| Field | Purpose |
| --- | --- |
| `terminalWireId` | The reserved synthetic terminal line ID of the existing emulator instance. |
| `destination` | A reserved fictional local number ending in the `555-01xx` range. |
| `port` | A free local TCP port distinct from the normal NCC listener. |
| `timeoutSeconds` | Bounded test duration, from 30 to 1800 seconds. |
| `readyFile`, `resultFile` | Distinct absolute local paths for readiness and redacted observations. |
| `tablePackageFile` | Optional private, hash-verified standard later Multi-pay twelve-record baseline. Enables a separate full INSTALL. |

From `backend/`, run only the manual fixture with the settings path in `COINLINE_MANUAL_CARD_LAB_SETTINGS`:

```sh
COINLINE_MANUAL_CARD_LAB_SETTINGS=/path/to/private/settings.json \
  dotnet test tests/HostPlatform.Tests.Integration/HostPlatform.Tests.Integration.csproj \
  --configuration Release --filter 'FullyQualifiedName~Isolated_synthetic_card_call'
```

CI does not activate the manual fixture. The automated fixture test verifies rejection of unrelated synthetic terminal, route, credential and format values and deduplication of authorization retries. Wait for the readiness file, then use an already installed emulator and direct only its NCC connection to the reported loopback port. Keep its local central-office simulator isolated. With no package, the fixture has no table assignment; verify the emulator's existing screening permits the selected test route before placing a call. If a private baseline package is supplied, the fixture verifies the record order, packed profile, wire identity and every source digest, then uses the typed editor on two empty screening slots and one unused tariff index. It publishes and assigns the complete twelve-record clone only in the isolated test database. The readiness file identifies the changed record types. No-op saves and readback are checked before the listener is released. The source records and shared host assignment are not modified. Do not redistribute private table payloads.

The readiness file confirms the exact quote preflight and listener reachability. The result file separates actual terminal quote requests, simulated authorization sequences, decoded CDRs, reported charge and an idempotent adjustment to the fixture's in-memory synthetic account. A quote alone does not debit the simulated account. A legacy CDR without a charge amount is insufficient to complete the simulated settlement test; no amount is inferred. No result represents processor settlement.