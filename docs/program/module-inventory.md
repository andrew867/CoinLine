# E-purse module inventory

The original Terminal Information configuration screen included separate SmartCity and Proton identifiers for the corresponding installed module. CoinLine records both in the phone's **Terminal inventory → E-purse module identifiers** section. An authorized inventory operator can enter, replace or explicitly clear either identifier without changing the other.

These are opaque administrative identifiers, not card account numbers, PINs, keys, application credentials, association data or proof that a module is installed. Preserve leading zeroes and the identifier's displayed form. The original help does not establish an exact wire encoding or length; the management field has a 64-character storage limit and accepts non-space, non-control characters. Do not enter payment credentials.

The registration has its own optimistic revision and is stored independently of the ordinary inventory profile. Updating the site or other inventory fields cannot erase it. Existing phones start with no registered identifiers. The inventory read and update permissions apply; audit records retain only which identifiers are present and the revision, not their values. No configuration assignment, MT record, NCC contact or payment request is created by this operation.

Actual module selection, application association, authorized stored-value processing and physical-terminal compatibility are independent acceptance gates.

The PostgreSQL regression verified leading zeroes, independent clear and update, stale revisions, read-only permissions, required request fields, and preservation after an ordinary inventory edit. It also checked that the audit contains no identifier values and no phone assignment, NCC session, download job or call authorization is created. The existing fresh-schema and legacy-upgrade comparisons passed with the additive inventory table; earlier phone configuration and payload bytes remained intact.
