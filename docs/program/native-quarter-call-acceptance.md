# Native quarter-call, upload and cashbox acceptance

An isolated, previously installed MTR 2.12 graphical terminal was used with a synthetic local-only call destination. No external voice route or payment provider was enabled. The normal configuration was copied to an independent published set and only MT50, MT73 and MT92 changed. The remaining fifteen record digests stayed identical. On an ordinary Craft refresh the terminal picked up the same three-record operator batch and returned all three application acknowledgements.

The host quote was set to 25 cents for one minute on the two exact synthetic dial forms, with unrelated destinations denied. The terminal's screening entry was coin-only with answer supervision and targeted CDR collection. The selected tariff requested host rating. A 20-cent cashbox value threshold made a single collected quarter a useful status test without changing unrelated coin settings.

## Observed result

The first call reached a 25-cent quote but the deposit window expired before a voice call was established. The synthetic quarter was returned. A separate second call received the same quote, displayed 25 cents, accepted one quarter, dialed the exact local route and received local answer supervision. After normal hangup and exit, the coin device retained 25 cents in the box, zero in escrow, and 25 cents refunded from the first attempt. These physical-model observations were not used to manufacture host accounting.

On normal warm restart, the terminal sent MT42 and the host replied MT17. The terminal sent its new MT53 call record with status and the end marker. CoinLine stored the record before acknowledging it, returned the call receipt and application end marker, completed the subsequent alarm exchange and observed normal clear. The one CDR reported 25 cents charged and 25 cents collected. No additional record was inferred for the expired first attempt.

Normal vault open, box removal, replacement and close caused a separate native MT51 collection report. The manager decoded the 71-byte report as a 25-cent collection, 25-cent removed-box balance, one coin and value-threshold-exceeded condition. It acknowledged the dialogue and the terminal cleared normally. The API retained the receipt-time acknowledged MT50 reference and did not expose the collector identifier. Immediately after collection the current replacement box remained unknown.

A later management contact supplied its own 57-byte MT38. The replacement box identified by the status matched the new box in the collection report, with zero cents, zero coins and normal condition. The current balance became zero only after that terminal report. After subsequent ordinary contacts, there were still two independent quotes, one paid-call CDR, one collection and one current status. The normal Craft call-record query reported no remaining records. No duplicate call or collection was observed; a separate TCP regression covers deliberate reconnect/replay.

| Evidence | Result |
| --- | --- |
| Native selected configuration | MT50, MT73 and MT92, 3/3 on original batch |
| Host quotes | Two separate 25-cent quotes, first attempt expired, second answered |
| Device coin model after collection | 25 cents refunded, 25 cents removed, 0 in escrow, 0 in box |
| MT53 CDR | One, 25 cents charged and 25 cents collected |
| MT51 collection | One, 25 cents collected, one coin; removed box threshold exceeded |
| MT38 current replacement | One, zero cents and zero coins, normal condition; matching replacement identifier |
| Follow-up contacts | No duplicate CDR or collection; normal clear |

This establishes native emulator behavior for the tested profile. It does not establish the reporting timing of every earlier or later device, calibrated physical coin acceptance, or stock-terminal bench certification. In a separate earlier trial, coin call details were present without a cashbox status or collection record, and the manager correctly did not calculate a box balance from them.
