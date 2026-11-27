# Regional rating and portable rate plan packages

Open **Rate plans**, then **Load Newfoundland and North American starter**. This prepares the following independent host rating template without changing an existing plan, customer, phone or table assignment.

| Dialed form | First charge (USD) | Following charge (USD) |
| --- | ---: | ---: |
| 709 followed by a seven-digit subscriber number, ten digits total | 0.25 for the whole call | No further charge |
| 1-709 followed by a seven-digit subscriber number | 0.50 first minute | 0.25 per started additional minute |
| Other active Canadian geographic area code, with leading 1 | 1.00 first minute | 0.25 per started additional minute |
| Active US geographic area code, with leading 1 | 1.50 first minute | 0.50 per started additional minute |
| 211, 311, 511, 611, 811 | Free | Free |
| 911 | Emergency, no charge | No charge |
| 411 | 1.00 for the whole call | No further charge |
| 711 | 0.25 per started minute | 0.25 per started additional minute |
| Toll-free 800, 833, 844, 855, 866, 877, 888 (with or without leading 1) | Free | Free |

Ordinary ten-digit destinations outside 709 and seven-digit destinations are not covered. A valid subscriber number must start with a digit from 2 through 9 in its exchange. Unknown geographic and non-geographic areas are not made free or payable by a catch-all rule. The Canadian 879 overlay follows the other Canadian tariff; only the explicit 709 area receives the local whole-call price. Existing centrally configured number classes still apply. In particular, blocked and emergency classes cannot be overridden by the starter. The exact 411 and 711 rules explicitly override matching free classes for this plan only; no previous plan or number class is edited.

Area code inventory: [NANPA Geographic Area and NPA Reports](https://www.nanpa.com/reports/npa-reports), [public NPA dataset](https://reports.nanpa.com/public/npa_report.csv), snapshot dated 2026-11-27. Includes 378 active US geographic NPAs (as classified in the report) and 55 active Canadian geographic NPAs. Future assignments and other NANP countries are deliberately excluded. These are host pricing rules, not proof of valid service availability or routing in every exchange.

## Import, export and version review

1. Load the regional starter or select a previously exported JSON rate plan. Optionally choose an existing published plan to retain its exact four-digit and 709 free-call exceptions. Broad free prefixes, unrelated geographic exceptions and existing ordinary prices are not copied.
2. Enter a name for the separate draft and preview its rule, tariff, prefix and time-band counts. Explicit paid-service overrides are highlighted.
3. Confirm creation, then open the new draft. The original plan and its published selection remain unchanged.
4. Review the exact rules and use the rating quote tool to check representative calls. Publish only when intended.
5. On a saved plan, select a version and choose **Export selected version as JSON** to share its host rating configuration. Importing the file always creates a separate unassigned draft.

The versioned package includes rating mode, rules and pricing structures, tariff descriptions, destination prefixes and time bands. Tariff references use local package keys and are remapped to new database identities on import. The package does not include account identities, customers, phones, credentials, call records, or opaque legacy diagnostic expressions. The recognized paid-service override is represented explicitly. Invalid or duplicate references and invalid prices or expressions are rejected. Existing arbitrary legacy data that does not fit the package format must be reviewed independently.

## Terminal configuration boundary

The host quote tool can distinguish ten-digit 709 from 1+709 when both are supplied verbatim. Actual phones may normalize their dialed digits before a real-time rating request. A compatible terminal requires separately reviewed MT62 dialing rules, MT92 screening, local and regional pricing records and MT73 tariff tuples to preserve the intended dialing form and collect the correct charge. The template itself makes no terminal table, plan assignment, provider or routing change. Explicit service and emergency routing must be qualified independently in a local-only bench, not by placing real emergency calls. The listed prices are the operator's demonstration tariff choices, not a claim of regulated pricing.

The five focused backend unit cases exercise every included area, both connected-minute and whole-call rates, exact service numbers, unknown and shortened dialing forms, retained local free exceptions and package references. An isolated real PostgreSQL test verifies authorized preview/import/export, read-only permission, invalid input, independent identities, preserved relationships and actual host quote output. Four focused Vitest cases, production Web build and the local Chromium draft import/export workflow also passed. No native terminal behavior is inferred from these results.

## Independent terminal qualification

An isolated Manager combining the regional plan and explicit per-phone published-plan selection passed 21 host quote and rejection controls. The earlier two-line Multi-Pay terminal received and acknowledged all eleven assigned configuration records, reached service and successfully restarted. A separate physical-input emulator run from that retained installation passed fifteen controlled cases using only a local CO, with no external calls.

| Native earlier-profile behavior | Observed result |
| --- | --- |
| Ten-digit 709 local | Requested $0.25, accepted one synthetic quarter, outpulsed all ten digits without rewriting, received local answer and returned on-hook. A separate duration run stayed locally answered for 130 emulated seconds without an additional payment request. |
| 1+709 | Displayed $0.50 before payment or outpulse. Two quarters paid the first period; a $0.25 continuation request and a third quarter extended the local answer to 90 emulated seconds. |
| 1+US geographic area | Actual real-time request received $1.50, displayed $1.50 before payment or outpulse. |
| 1+other Canadian geographic area | Actual real-time request received $1.00, displayed $1.00 before payment or outpulse. |
| Nonlocal ten-digit and seven-digit destinations | Denied without outpulse. |
| 411 and 711 | Displayed $1.00 and $0.25 respectively before payment or outpulse. A separate simulated local 711 call accepted one quarter initially and requested a further $0.25; another quarter extended the local answer to 90 emulated seconds. |
| 211, 311, 511, 611, 811, one toll-free number and one exact information-service extension | Outpulsed without requesting payment. |

Both national real-time decisions used the selected published plan and restored the national prefix from normalized terminal requests. The received MT64 bodies had matching CRCs and contained 60-second first and following periods and durations, with charges 150/50 cents for US and 100/25 cents for Canada. The final delimiters were not present in this particular diagnostic capture, so it is not a complete raw-session capture. The actual initial prices were displayed; longer paid-call collection for these two national destinations has not been exercised. Whole-call 411 pricing over a longer assumed duration was verified at the host. Emergency classification was checked in the host only, and no emergency call was placed. All duration controls used isolated local CO audio, not a public relay or directory service.

The later graphical profile is not qualified for general paid calling with this regional candidate. Its installed tariff and screening records matched the intended bytes, but ordinary paid controls outpulsed without requesting payment. A read-only device observation showed the intended inter-area rate selection being replaced before tariff lookup, while the directory-assistance control displayed the expected charge. Continue the later-profile diagnosis independently. Do not publish the graphical candidate as a verified paid-call configuration. These observations are emulator evidence, not physical bench acceptance.
