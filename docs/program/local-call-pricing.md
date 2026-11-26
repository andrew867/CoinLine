# Configuring local-call pricing

CoinLine has two independent kinds of rate configuration. A **terminal tariff** (MT73) is a record downloaded to a phone. It is selected by terminal screening and local dialing records. A **host rate plan** is the configuration used for real-time rate queries (MT63/64). Editing one does not automatically change the other.

## Local charge stored on a phone

1. Open a draft configuration set and choose **MT73, Terminal tariffs and rate ladders**. If the set is published, make a draft copy first.
2. Select the rate row referenced by your local call screening or exchange entry. In **Local-call pricing**, choose one of the three structures and enter the charges in cents.
3. Select **Apply pricing to this row**. Review the classification and exact initial and additional periods and charges below the helper. Save the record, review the route references and publish the configuration.
4. Assign or queue the required selected records for the phone's installed firmware. Verify the application's table acknowledgements, then test the actual call and its displayed charge. If the route requests real-time rating, the host plan controls its quote instead.

| Pricing | Example at 1 minute | At 5 minutes | MT73 fields |
| --- | --- | --- | --- |
| One charge for the whole call | $0.25 | $0.25 | Fixed local charge, unlimited initial period, 25 cents initial charge, zero additional period and charge |
| Same charge for every minute | $0.25 | $1.25 | Local measured service, 60 seconds and 25 cents for both steps |
| Different first minute and following minutes | $0.25 | $0.65 | Local measured service, initial 60 seconds at 25 cents, subsequent 60 seconds at 10 cents |

The third choice treats the first-minute amount as the total including connection. The period preview illustrates whole-minute charges, not actual call billing, voice prompts or carrier surcharges. The unlimited initial period corresponds to the terminal's signed all-ones period value, not zero seconds. The legacy Rate Table help defines that choice as a single initial charge for the duration of a call.

Applying any preset only stages the five typed fields in the selected local tariff row; nothing is transmitted until the normal configuration workflow. Other classifications and custom periods remain available in the advanced fields. The exact preset bytes and preservation of neighboring rows are tested for recognized MT73 layouts. Native verification of each new pricing pattern, including a call beyond the first minute, remains separate.
