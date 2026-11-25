# Security boundaries

- **API keys** (`X-API-Key`) or development operator middleware — see [Authentication](../api/authentication.md).
- **Roles**: Operator / Technician / Admin policies on selected routes.
- **Named account profiles**: Optional screen permissions restrict management controller actions by read, update, add and delete operation. They do not replace role policies or deployment keys.
- **Raw payloads**: Sensitive — replay export requires explicit confirmation.
- **Cards**: PCI scope terminates upstream — see [Payment card boundary](../security/payment-card-boundary.md).
