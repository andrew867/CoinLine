# High availability

**Current posture:** single-instance API + PostgreSQL with standard HA via external Postgres clustering (Patroni, cloud RDS). API is stateless; scale horizontally behind load balancer **when session affinity not required** (verify auth/cache assumptions).

`SubsystemHeartbeats` supports worker readiness — see [Observability](../backend/observability.md).
