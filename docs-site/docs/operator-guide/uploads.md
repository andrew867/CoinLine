# Uploads

Open **Upload batches** to review call details received over NCC and manually imported batches. Choose a **Terminal call detail** row to see the phone, dialed digits, call type, payment type, start time, duration, amounts reported by the terminal, and record sequence. The start time follows the phone's clock. Amounts remain in terminal units.

CoinLine records a complete supported call detail before sending its sequence receipt. A retry can receive the receipt again for the same stored record. Card numbers and authorization bytes are omitted. For a combined record from a phone with an unverified number layout, CoinLine displays the common dialed prefix if the layout cannot establish the remaining digits.

Use the operator review note on either upload source. Use **Run ingest** or **Reprocess** on manually imported batches to derive records from the stored input.
