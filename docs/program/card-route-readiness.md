# Review magnetic card call readiness

The table-set **Provisioning review** now distinguishes a configured magnetic card and a working paid route. A successful swipe alone does not establish that the selected carrier accepts the card or that the number will proceed to host authorization.

For the verified earlier and later Multi-pay records:

- MT22/MT134 describes magnetic card ranges and whether host validation and credit-card processing are intended.
- MT23/MT135 has its own carrier selection and acceptance bitmap. Review the range's carrier as well as the default credit-card carrier and the later local/intra-area defaults. The Manager identifies missing or incompatible acceptance bits, and asks for review of a nonzero code on carrier reference zero when the local exchange is intended.
- MT92 can explicitly restrict an otherwise paid route to coins. The review flags contradictory card/coin settings and the case where every included explicit paid rule excludes cards, without claiming that unscreened routes are also blocked.
- A referenced MT73 host-rated tariff requires a compatible MT62 response classification. The review flags a found rate mapped to a denied call or no payment. This applies to explicit paid routes and does not predict the complete native numbering path.

These findings are advisory and do not modify a set. A partial set may legitimately rely on previously installed records, and coin-only routes may be intentional. Check the actual terminal's selected carrier, number classification, host request, authorization outcome, answer and separate settlement evidence before claiming a working card-paid call.
