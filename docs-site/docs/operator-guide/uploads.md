# Uploads

Open **Upload batches** and use **Source** to find terminal call details, terminal statistics or manual imports. Choose a **Terminal call detail** row to see the phone, dialed digits, call type, payment type, start time, duration, amounts reported by the terminal, and record sequence. The start time follows the phone's clock. Amounts remain in terminal units.

CoinLine records a complete supported call detail before sending its sequence receipt. A retry can receive the receipt again for the same stored record. Card numbers and authorization bytes are omitted. For a combined record from a phone with an unverified number layout, CoinLine displays the common dialed prefix if the layout cannot establish the remaining digits.

Choose a **Terminal statistics** row to inspect its reporting period and decoded counters. Use **Statistics report** to filter by the report's name and message type. Communications reports include connection failures and the seven timing buckets configured by the terminal. Call statistics include call counts, duration and repeat-dialer use. Carrier reports identify each included carrier by its terminal reference number. A terminal can send several carrier reports for the same period. Messaging reports are available from compatible terminals.

For **Graphical usage statistics (MT153)**, the detail shows the phone-clock reporting period and the reported global screen number, screen owner code, six softkey owner codes with their press counts, and the aggregate direction-key press count. Each 245-byte record carries up to eight screen entries. A period covering more screens arrives in separate records with the same dates. Retries of the same record do not create another upload. These are not advertisement impression or completed-call counts; presses from mixed panels are aggregated by the containing screen and key position. The report retains the owner codes captured by the terminal for that period, rather than substituting current table-set values.

Use the operator review note on any upload. Use **Run ingest** or **Reprocess** on manually imported batches to derive records from the stored input.
