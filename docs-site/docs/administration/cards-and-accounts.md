# Cards and accounts administration

**CoinLine Server** exposes **card products**, **accounts**, balances, transactions, smartcard types, and **reconciliation batches** for payment-adjacent workflows. Defaults are **simulation-oriented** until your organization completes scope and field validation.

## Simulation banner

The API exposes **`GET /api/cards/simulation-state`** for UI banners. Production cardholder-data environments require a formal **PCI** scope assessment independent of this documentation.

## Operator tasks

- Maintain **card products** and catalog metadata.
- Open and monitor **accounts**; review timelines and balances.
- Post **reconciliation** state transitions only with appropriate authorization (confirm-guarded paths).

## Related

- [Operator — Cards](../operator-guide/cards.md)
- [Security — Payment card boundary](../security/payment-card-boundary.md)
- [Protocols — Cards, smartcards, e-purse](../protocols/cards-smartcards-epurse.md)
