# Cards, smartcards, e-purse

The card ledger APIs support simulation. The NCC listener returns MT3 for a formatted card authorization request (MT1) and MT65 for an extended request (MT2) from a matched terminal. Both replies currently decline because there is no connected payment authorization provider. These requests do not start a download.

MT22/134 and MT93 are provisioning records. They configure the terminal's local card policy and can be sent in a separate MT44 download. Local card acceptance at the terminal does not establish host authorization or settlement.

The live listener saves session metadata and omits raw bytes for directly received MT1, MT2, MT4 and MT53 records. Live card call detail ingestion and its MT5 application acknowledgment remain open.


## Reference smartcard application matrix

The inspected MTR 2.12 standard and graphical Multi-pay configurations enable GPM103 synchronous disposable-card handling. Other applications listed below are distinct family/subtype entries, not interchangeable account classifications.

| Application | Contact transport | Purse | In inspected configurations |
| --- | --- | --- | --- |
| GPM103 | Synchronous memory | Disposable | Application and handler included |
| Smart City GPM416 | Synchronous memory | Disposable | Disabled |
| Smart City PCOS | Asynchronous processor | Reloadable | Disabled |
| Smart City MPCOS | Asynchronous processor | Reloadable | Disabled |
| Proton ORCA | Asynchronous processor | Reloadable | Disabled |
| Mondex | Asynchronous processor | Reloadable | Disabled; historical/partial |
| Visa Cash disposable | Asynchronous processor | Disposable | Disabled; historical/partial |
| Visa Cash reloadable | Asynchronous processor | Reloadable | Disabled; historical/partial |
| Card Touch Technology | Asynchronous processor | Reloadable | Application dispatch disabled |

The Web console's **Smartcard types** page shows this reference matrix separately from the stored host classification catalogue. No native smartcard payment application has been qualified by this matrix. Reader contact or generic asynchronous transport testing does not prove scheme compatibility, stored-value debit, reload, or settlement. MT93 provisions acceptance parameters; it is not a personalization or issuance interface.

An authorized reference-card or test-module experiment must establish recognition, appropriate call authorization, actual connected charges, card removal and exhaustion, interruption recovery, and independent reconciliation. Disposable cards do not imply a reload workflow. A security module or generic rechargeable transport is infrastructure, not another customer purse. Magnetic Visa/Mastercard support does not establish EMV chip-payment acceptance.
