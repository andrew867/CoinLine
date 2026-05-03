# Craft API

**Route prefix:** `/api/craft`

| Method | Path |
|--------|------|
| GET | `/api/craft/command-types` |
| GET/POST | `/api/craft/sessions` |
| GET | `/api/craft/sessions/{id}` |
| POST | `/api/craft/sessions/{id}/commands` |
| POST | `/api/craft/commands/{id}/simulate` |
| GET | `/api/craft/commands/{id}` |
| POST | `/api/craft/commands/{id}/cancel` |

!!! warning "SIMULATION ONLY"
    Live modem craft transport **`HARDWARE VALIDATION REQUIRED`**.

**UI:** `/craft`, `/craft/:sessionId`, `/craft/commands/:commandId`
