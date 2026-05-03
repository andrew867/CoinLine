# Versioning policy

- **API:** Document as **v1** OpenAPI — breaking HTTP changes require changelog entry + migration notes.
- **Database:** EF migrations are sequential — avoid destructive rollback without DBA review.
- **Docs site:** Version together with tagged releases when publishing externally.
