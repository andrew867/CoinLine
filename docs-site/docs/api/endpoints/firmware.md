# Firmware API

**Route prefix:** `/api/firmware`

Packages, artifacts, compatibility rules, targets, jobs:

| Area | Examples |
|------|----------|
| Policy | `GET /api/firmware/execution-policy` |
| Catalog | `GET/POST .../packages`, `GET .../packages/{id}`, artifacts POST |
| Versions | `GET/POST .../versions` |
| Targets | `GET/POST .../targets` |
| Jobs | `GET/POST .../jobs`, `GET .../jobs/{id}`, `simulate`, `approve`, `cancel` |

!!! danger "Live flashing"
    **`Firmware:AllowLiveFlashing`** default **false**. DLA/XMODEM **`HARDWARE VALIDATION REQUIRED`**.

**UI:** `/firmware/packages`, `/firmware/jobs`, etc.
