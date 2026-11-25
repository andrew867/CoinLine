# Lab data partition

`postgres/` and `payloads/` are the durable disk for this Compose lab. They are bind-mounted into the containers. Do not commit them.

| Directory | Container path | Role |
| --- | --- | --- |
| `postgres/` | `/var/lib/postgresql/data` on the `postgres` service | PostgreSQL data directory |
| `payloads/` | `/var/coinline/payloads` on the `api` service | Filesystem root for large opaque blobs (`Platform__PayloadStorage__RootPath`) |

`docker compose up` creates a missing directory. Database files and payload bytes stay on this machine. Back up this folder if the lab inventory should survive a new checkout.

Leave `postgres/` empty until the database initializes. Files already in that directory stop PostgreSQL from creating its data directory.

The API process runs as uid 10001. `payloads/` must be writable by that user. If readiness reports payload storage not writable, fix ownership on the mount:

```bash
docker compose exec -u root api chown 10001:10001 /var/coinline/payloads
```
