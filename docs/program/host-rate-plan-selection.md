# Host rate plan selection

The customer console now offers a **Default host rating plan**, and the phone detail page offers a **Host rating plan for this phone**. Each lists published real-time plans for that customer and shared published real-time plans. An unpublished or another customer's plan cannot be selected.

The selection order on a genuine NCC real-time rate request is:

1. An explicit phone plan.
2. An explicit customer default, inherited by active phones without a phone selection.
3. The existing automatic customer behavior, which prefers a published plan named Local default and otherwise chooses a customer plan by name.

An existing installation has no explicit assignments after the additive database migration, so its existing selection behavior is preserved. An unavailable explicit selection does not silently fall back to a differently priced plan. The quote is unavailable unless the centrally configured number classes determine an emergency, free or blocked outcome. Clear a phone override to inherit its customer. Clear a customer override to return to its prior automatic policy.

Review the effective plan and inherited phone count, choose a published plan and explicitly confirm saving. Current and previously recorded requests are not recalculated. The Manager binds the actual quote to the loaded published version, records the source of the selection in the quote audit, and refuses to use a different version if the selection changes during quote preparation. Assigning the plan is independent of terminal-local numbering, screening and tariffs. It does not prepare a download, update a table set, or authorize a payment.

An isolated PostgreSQL integration verified the selection precedence over actual loopback NCC rate requests from two registered phones. Nine requests were acknowledged and returned the expected exact MT64 rate or unavailable result; their recorded applied version IDs match the selected published snapshots. Unpublished and foreign plans, stale selection changes and a read-only operator's writes were refused. Removing the explicit selections restored existing behavior, and no table batches were created. The existing call-pricing and legacy-database migration regressions also passed (seven focused PostgreSQL tests in total). Three focused Vitest cases, production Web build and the customer selection Chromium workflow passed.

Native terminal display, payment behavior and local-table alignment must be qualified separately before changing the rate plan on an operational phone.
