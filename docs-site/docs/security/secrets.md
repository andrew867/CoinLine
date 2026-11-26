# Secrets

Use `COINLINE_OPERATOR_TOKEN_KEY` or `Security:OperatorTokenKey` for the operator signing secret (at least 32 bytes). Set bootstrap administrator credentials only during first provisioning. For local Development, user-secrets are supported by the API project. See [Environment variables](../reference/environment-variables.md).

Never commit production keys — use env vars / vault injection.
