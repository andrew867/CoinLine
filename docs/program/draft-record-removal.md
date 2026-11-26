# Removing a draft configuration record

Open an unpublished configuration set and find the unwanted record in **Versions in this set**. An operator with Table records Delete permission can review and confirm its removal. The request names the exact draft, record identity, set revision, record revision and stored payload hash. If any of those change before confirmation, the server rejects the request.

This is useful when a draft no longer needs an optional table, or when an old or incompatible record was imported under the wrong definition. It removes only that draft record. Other records, the normalized payload blob and all previous configuration sets are retained. It does not alter an assigned phone or send a removal to a terminal.

Published sets remain immutable. A record cannot be removed if another record in the draft depends on its definition, or the draft or record has retained assignment, download, schedule, override or identity-transition references. Make a new draft copy for an operational change, review the resulting requirements separately, and use the normal publish and rollout workflow.

This operation is distinct from blanking or deleting an entry inside a call-screening list, and from explicitly clearing an optional per-phone assignment. None of those actions is implied by removing a record from a draft.
