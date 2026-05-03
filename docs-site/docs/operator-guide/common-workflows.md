# Common workflows (CoinLine Management Console)

Task-oriented steps for operators using **CoinLine Management Console** against **CoinLine Server**.

## Provision and configure

1. **Customer → Site → Terminal** — create hierarchy under [Customers & sites](customers-sites.md) and [Terminals](terminals.md).
2. **Table definitions → versions → set → publish** — see [Table management](table-management.md).
3. **Assign table set** to the terminal and **start download batch** — [Downloads](downloads.md).
4. **Inspect activity** — DLOG / uploads / audit as needed ([DLOG viewer](dlog-viewer.md), [Audit log](audit-log.md)).

## Rating and calls

1. Configure **rate plans** and publish approved versions — [Rating](rating.md).
2. Use **rating quote** tools for validation; record **call records** when exercising billing flows.

## Cards (simulation-capable)

1. Review **`/api/cards/simulation-state`** banner behavior — defaults are **simulation-first**.
2. Manage **products**, **accounts**, and **reconciliation** only within your approved **PCI** scope — [Cards](cards.md).

## Firmware packages

1. Register **packages**, **targets**, and **jobs** per your rollout process — [Firmware jobs](firmware-jobs.md).
2. Treat live programming paths as **gated** until your field program authorizes them.

## Quickstart reference

See `README.md` in the product package for local **CoinLine Server** + console startup commands.
