# Uploads

Open **Upload batches** and use **Source** to find terminal call details, terminal statistics or manual imports. Choose a **Terminal call detail** row to see the phone, dialed digits, call type, payment type, start time, duration, amounts reported by the terminal, and record sequence. The start time follows the phone's clock. Amounts remain in terminal units.

CoinLine records a complete supported call detail before sending its sequence receipt. A retry can receive the receipt again for the same stored record. Card numbers and authorization bytes are omitted. For a combined record from a phone with an unverified number layout, CoinLine displays the common dialed prefix if the layout cannot establish the remaining digits.

Choose a **Terminal statistics** row to inspect its reporting period and decoded counters. Communications reports include connection failures and the seven timing buckets configured by the terminal. Call statistics include call counts, duration and repeat-dialer use. Carrier reports identify each included carrier by its terminal reference number. A terminal can send several carrier reports for the same period. Messaging reports are available from compatible terminals.

Use the operator review note on any upload. Use **Run ingest** or **Reprocess** on manually imported batches to derive records from the stored input.
