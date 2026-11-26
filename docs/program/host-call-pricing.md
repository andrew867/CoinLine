# Host call pricing

Open **Rate plans**, choose the customer plan used by the phone, create or copy a draft version, and select a rated rule or reusable tariff. The pricing selector offers:

| Structure | Example configuration | 1-minute estimate | 5-minute estimate |
| --- | --- | --- | --- |
| Whole call | $0.25 once | $0.25 | $0.25 |
| Same charge every started minute | $0.25 per minute | $0.25 | $1.25 |
| Different first and following minutes | $0.25 including connection, then $0.10 per minute | $0.25 | $0.65 |

In the last structure the first-minute total includes the connection charge. New metered estimates charge every started minute; existing per-minute rules retain their historical proportional-duration estimates until explicitly converted. Publishing makes the draft's rules, destination prefix tariffs and time-band overrides active. Rules take precedence over the tariff catalogue, as before. Use **Price a call** with different durations to check which structure and amount apply. An estimate is not a deposited amount, a collected coin total or a completed call charge.

Real-time terminal rate responses transmit the actual initial and subsequent charge schedule, not the duration-based estimated total. The flat response uses the unlimited initial period and zero subsequent charge; equal and split rates use 60-second charging steps. Exact wire fields and a TCP fixture can establish what the host offers. A compatible native terminal must still be observed through a call lasting beyond the first minute before those new real-time rate structures are considered qualified on that terminal.

The database migration adds separate pricing-mode and initial-charge fields without rewriting existing per-minute values or published versions. No setting is automatically converted from earlier pricing. A terminal using offline local tariffs still requires its normal MT73 configuration and route selection; a host plan does not change a terminal's stored tariff table.
