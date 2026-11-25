# Cards, smartcards, e-purse

The card ledger APIs support simulation. The NCC listener returns MT3 for a formatted card authorization request (MT1) and MT65 for an extended request (MT2) from a matched terminal. Both replies decline by default. An explicitly registered trusted provider can supply a decision. CoinLine also has an opt-in synthetic lab provider restricted to its designated test fixture, terminal, test account and reserved fictional route. These requests do not start a download.

MT22/134 and MT93 are provisioning records. They configure the terminal's local card policy and can be sent in a separate MT44 download. Local card acceptance at the terminal does not establish host authorization or settlement.

The live listener saves session metadata and omits raw bytes for directly received MT1, MT2, MT4 and MT53 records. Authorization decisions are persisted as redacted audit metadata. The synthetic provider associates a recent paid native MT63/64 quote with one authorization decision, so a reconnect with the same call sequence can reuse the decision without creating a payment. No approval debits an account. Live card call detail ingestion and its MT5 application acknowledgment remain open.
