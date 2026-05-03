# Production hardening checklist

- [ ] `Security:Mode=ApiKey` with keys via secrets manager
- [ ] TLS via reverse proxy
- [ ] Postgres network isolation
- [ ] `Firmware:AllowLiveFlashing=false` unless explicitly certified
- [ ] Card simulation banner acknowledged operationally
- [ ] Backups encrypted + tested restore
- [ ] Metrics endpoint restricted by network policy
