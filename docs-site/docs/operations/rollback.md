# Rollback

- **API/UI:** redeploy previous artifact.
- **DB:** restore backup or reverse migration **only** with DBA review — forward-only migrations preferred.
- **Firmware jobs:** use cancel APIs — EEPROM state may require field rollback (**operator-defined**).
