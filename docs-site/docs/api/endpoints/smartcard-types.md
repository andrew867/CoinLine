# Smartcard types API

**Routes:**

- `GET /api/smartcards/types`: existing database-backed `SmartcardType` host classifications.
- `GET /api/smartcards/applications`: read-only reference application matrix, independent of the installed account catalogue. Each entry has a stable application key, family, subtype, contact transport, scope-specific enablement, implementation evidence and payment qualification. A nullable host classification is not a card model ID or a native wire enumeration.

Both routes require the Smartcard types read permission. No issuer data, personalization, or payment credentials are returned. The matrix is not a query of the currently running firmware.

See the [reference application matrix](../../protocols/smartcard-applications.md) for the evidence and qualification scope.

**UI:** `/smartcard-types`
