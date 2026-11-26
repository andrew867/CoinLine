# Card authorization deadlines and redacted diagnostics

Card authorization is an immediate, separate dialogue. A provider decision does not itself settle a call or post a payment. The default remains decline-only when no provider is configured.

`CardPayments:AuthorizationTimeoutMilliseconds` sets the maximum wait for each provider invocation (default 5000, range 1 to 30000). The provider receives a cancellation token and an independent scoped service context. A late result is ignored; retransmitted requests receive the same cached wire decision, not a fresh authorization. Providers must honor cancellation and use their own durable idempotency for external operations. A timeout does not prove that an external reservation was never made.

The session audit separates timeout, HTTP failure, invalid provider response, other provider failure and explicit decline. Malformed immediate requests expose only their application-byte count and up to three recognized call-detail message types at established suffix record boundaries. Truncated known suffixes may identify their type but are never acknowledged or stored as call details. Unknown material is not examined beyond the failed boundary. Neither raw card data nor exception messages appear in this diagnostic.

The operator's payment history distinguishes authorization attempts from completed payment transactions. An external provider remains a separate integration and settlement-qualification milestone.
