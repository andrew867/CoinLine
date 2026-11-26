# Production hardening checklist

- [ ] Named operator authentication in every environment; production signing key via secrets manager
- [ ] Explicit initial administrator; no default demo credential
- [ ] If using `Security:Mode=ApiKey`, deployment keys via secrets manager
- [ ] Loopback API binding or explicitly secured ingress
- [ ] TLS via reverse proxy
- [ ] Postgres network isolation
- [ ] `Firmware:AllowLiveFlashing=false` unless explicitly certified
- [ ] Card simulation banner acknowledged operationally
- [ ] Backups encrypted + tested restore
- [ ] Metrics endpoint restricted by network policy
