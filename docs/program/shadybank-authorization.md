# ShadyBank merchant authorization

CoinLine can use the scoped ShadyBank merchant instrument API to reserve funds for a recognized synthetic magnetic-card request and a recent matching native quote. It is an independent opt-in alternative to the existing synthetic ledger provider. Both are disabled by default; select only one provider per host instance to avoid ambiguous settlement policy.

Configure the bank's separate merchant service and an authorized opaque instrument token first. The provider sends only the token, quoted amount and a deterministic request UUID; it never sends the terminal's PAN, raw track, PIN, NCC frame or dialed number. The service key and instrument token belong in environment variables or user-secrets, never in committed settings. HTTPS is required except for an explicitly enabled loopback-only lab connection. Redirects and cookies are disabled.

| Configuration key | Meaning |
| --- | --- |
| `CardPayments__ShadyBankAuthorization__Enabled` | Explicit opt-in |
| `CardPayments__ShadyBankAuthorization__ServiceUrl` | Exact merchant authorization endpoint ending in `/api/merchant/authorize` |
| `CardPayments__ShadyBankAuthorization__ServiceKey` | Private scoped merchant key |
| `CardPayments__ShadyBankAuthorization__AllowLoopbackHttp` | Allow HTTP only at a loopback endpoint in a local lab |
| `CardPayments__ShadyBankAuthorization__Bindings__0__TerminalId` | Explicit registered synthetic terminal UUID |
| `CardPayments__ShadyBankAuthorization__Bindings__0__Destination` | Exact reserved synthetic test route |
| `CardPayments__ShadyBankAuthorization__Bindings__0__InstrumentToken` | Bank-issued private opaque instrument token |
| `CardPayments__ShadyBankAuthorization__Bindings__0__AllowAuthorizationOnlyRate` | Disabled by default. For a qualified legacy synthetic MT2 route that sends authorization before a separate terminal quote, allow an initial one-minute charge from the effective published host plan |

The host durably claims a quote for its original request type and call sequence before invoking the bank. The same quote and request reuse the bank UUID across reconnects; different sequences cannot reuse the quote. Same-socket RETRANS replays the cached response. A timeout, HTTP error or malformed bank decision declines the terminal and is separately audited. A timeout is not proof the remote reservation did not occur; it must be reconciled before settling or releasing funds.

For the optional legacy MT2 binding, a normal recent terminal quote still takes precedence. If no quote exists, CoinLine resolves the effective published host rate plan for the actual bound route, requires an allowed positive initial charge, and records that price and version in a separate durable authorization sequence claim. It does not create an MT63 or call record. The same terminal, route and sequence keep one reservation UUID through retries, reconnects and later terminal quotes. An expired or ambiguously reused sequence is declined rather than charged again. This conservative mode is only for isolated synthetic routes whose terminal behavior has been reviewed. It is not a general card-payment fallback or a way to bypass the bank balance check.

An approval does not post a CoinLine payment or debit its ledger. The bank's reserve-only decision and subsequent capture are separate. Automatic capture after a genuine uploaded CDR is not enabled by this provider. The current terminal credential recognizer is restricted to the existing synthetic magnetic-card fixture. The generic bank client accepts only opaque instruments and is reusable for independently validated smart-card applications; this does not establish historical Proton, stored-value or other issuer application acceptance. Those paths require their own qualified application contract and outcome evidence.

The automated regression uses real PostgreSQL and an in-process merchant HTTP fixture. An additional explicit lab test can contact an isolated loopback bank using `COINLINE_SHADYBANK_LAB_SETTINGS` (private JSON with `merchant_service_key` and `instrument_token`), `COINLINE_SHADYBANK_LAB_URL`, `COINLINE_SHADYBANK_LAB_RESULT` (sanitized request ID and outcome) and the disposable PostgreSQL test connection. It is skipped by default. Use an independent synthetic account and reconcile or release the resulting reservation after the test.

The isolated real-bank bench passed a five-cent native-shaped TCP authorization and exact reconnect replay. The bank retained one masked reservation, the posted balance and CoinLine payment ledger remained unchanged, and the ordinary bank void operation restored the full synthetic available balance. This is a TCP fixture and real bank service qualification, not a physical or emulated terminal payment acceptance claim.
