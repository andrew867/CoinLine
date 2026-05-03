# Security boundaries

- **API keys** (`X-API-Key`) or development operator middleware — see [Authentication](../api/authentication.md).
- **Roles**: Operator / Technician / Admin policies on selected routes.
- **Raw payloads**: Sensitive — replay export requires explicit confirmation.
- **Cards**: PCI scope terminates upstream — see [Payment card boundary](../security/payment-card-boundary.md).
