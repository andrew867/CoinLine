# Cards, smartcards, e-purse

The card ledger APIs support simulation. The NCC listener returns MT3 for a formatted card authorization request (MT1) and MT65 for an extended request (MT2) from a matched terminal. Both replies currently decline because there is no connected payment authorization provider. These requests do not start a download.

MT22/134 and MT93 are provisioning records. They configure the terminal's local card policy and can be sent in a separate MT44 download. Local card acceptance at the terminal does not establish host authorization or settlement.

The live listener saves session metadata and omits raw bytes for directly received MT1, MT2, MT4 and MT53 records. Live card call detail ingestion and its MT5 application acknowledgment remain open.
