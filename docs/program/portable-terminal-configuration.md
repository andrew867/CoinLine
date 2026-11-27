# Portable terminal configuration packages

Open a table set, then **Share or import configuration**. Export a reusable JSON package from a registered profile with a verified native layout. The portable format uses record numbers, profile ID, download sort order, dependency record numbers, exact packed payloads and SHA-256 digests. It is not a database or fleet backup.

## Share reusable configuration

Export includes supported terminal features, text and graphical advertising, prompt controls, communication and coin settings, quick access, service restrictions, tariffs, screening, numbering plan, local dialing areas and later rate sets, where present in the matching verified profile. Unknown or unreviewed records are omitted.

The package intentionally excludes phone identity (MT21), payment and carrier records, stored-value parameters (MT93), optional messaging/IOPR records, and the code download schedule (MT94). Craft access and desk key-card fields in MT31/MT62 are blanked in the package. MT35's date and one-time clock adjustment fields are also omitted from the shared bytes. Host-side assignment identifiers, card-master links, screening labels, customer/site references, serial numbers and source database IDs are not exported.

Review the package before sending it to another operator. Advertising artwork, prompt text, dial destinations, carrier-related numbering rules and other user-authored settings may include private or restricted content even without the excluded fields.

## Import to a different installation

1. Choose an existing local table set with the same verified profile and the intended local access and payment configuration. It can be a draft or a published base.
2. Open **Share or import configuration**, select the JSON file and enter a new draft name.
3. Preview the import. The Manager validates the package schema, unique record numbers, exact lengths, message types, digests, local-field blanks and dependency order. It remaps dependencies to local definitions. If an imported record is not in the base, register an unambiguous local definition for it first.
4. Review the included, unchanged and replaced records, the retained local fields, omitted records that are missing locally, and firmware compatibility findings.
5. Explicitly confirm creating the draft. Review and prepare any missing required data using the ordinary native editors. Only then publish, assign and request delivery in the normal workflow.

The Manager restores the protected local fields from the selected base, never from the package. It retains other local records, updates only the reusable portion, and preserves exact bytes of unredacted imported records. The source and local base remain unchanged, and no assignment or download is created. Changed imported records receive new local revisions and do not inherit obsolete assignment identifiers or slot descriptions.

An unmodified export/import/export round trip retains the portable content independently of local credentials. Delivery, acknowledgements and actual terminal operation remain separate checks.
