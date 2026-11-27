# Review a dialed number

In a terminal configuration set, open the **Dialing, screening and service** or **Rates and local dialing** panel and expand **Review a dialed number across saved tables**. This is available to operators with table-record read permission, including on published sets. Enter the digits exactly as the terminal receives them. Review the ten-digit and leading-1 forms separately if the terminal may use either.

The review lists independent saved settings rather than declaring the call free, paid or rejected:

| Record | What the review shows |
| --- | --- |
| MT26 | Default rate-request payment classification and, on the later profile, area-code and international set rating enablement. |
| MT62 | Configured rate-response call/payment classifications and dialing nodes marked No payment. The review does not traverse the rules. |
| MT92 | Numeric screening patterns matching the exact digits, including the declared single-position and trailing wildcard lengths. All candidates are listed in wire order, with payment, free, carrier and restriction indicators. A retained rate on a no-payment rule is not reported as an ordinary paid tariff reference; a later free voice rule can separately reference a conditional data-port tariff. |
| Local dialing segments | Matching area-code and exchange entries for ten-digit NANP numbers, optionally with a leading 1. More than one matching segment is flagged for review. |
| MT150 | The configured area-code set rating code, including host and reject actions, separately from local exchange indices. |
| MT151 | When destination digits after the access prefix are supplied explicitly, the matching numeric international prefixes in wire order, their direct or fallback codes, and the independent no-match action. |
| MT73 | The configured classification, initial and additional periods and integer-cent amounts for referenced tariff rows. An unlimited period is shown separately from a per-minute charge. |

Earlier-profile direct free rules with a non-255 rate field are flagged for review instead of being assumed to follow the unrated branch. Later-profile toll-free voice and a conditional rated data-port call are separate paths; a retained rate can be intentional. A referenced row still does not establish the actual call price.

For international review, keep the complete dialed digits in the first field and enter the destination digits after the international access prefix in the optional second field. The review does not infer which digits MT62 removes. MT151 direct codes 0 and 1 describe host rating and rejection, while codes 2 to 99 reference MT73 rows 30 to 127. A destination with code 100 or higher uses the configured fallback action; when that action uses default code 0 or 1, the destination-fallback path references MT73 row 28 or 29 rather than the direct host/reject sentinel. A no-match default setting is shown separately. Multiple matching prefixes and disabled or unknown MT26 rating enablement are advisory, not a selected price.

For instance, an exact toll-free screening entry can coexist with a fixed-price local exchange and a regional reject action. Those are separate inputs, not interchangeable indices. Review the applicable dialing rule and actual device result before changing any broad payment default. A missing or unreadable record is not evidence that a destination is free or unrestricted.

Operators with rating and rate-plan read permission can open **Compare a published host quote for these digits** after the saved-record review. The quote tool receives the exact typed digits without including them in the URL. Choose a published host plan explicitly when more than one is available; the selected plan supplies its configured rating mode and optional customer scope, which can be reviewed or changed before quoting. Nothing is rated automatically. Test another duration to compare fixed or measured charges, and note that the host quote still does not establish which offline tariff the terminal selects.

The review is deliberately limited to registered, verified native record profiles. It does not evaluate graph traversal, special screening translations, precedence, carrier/service eligibility, discounts, international classification, host rating, or the terminal's currently downloaded revision. It neither changes nor publishes a configuration, does not start a download or dial a call, and uses only saved records, not unsaved edits. Test terminal behavior separately on an appropriate bench target.
