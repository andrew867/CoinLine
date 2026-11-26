# Individual phone configuration assignment parity

The Manager terminal information workflow includes individual configuration families and read-only lookup by configuration ID. CoinLine provides the phone-centric worksheet alongside whole-set assignment and the existing group/region selected-record rollouts.

| Capability | UI and API | Encoding and persistence | NCC acknowledgement | Terminal behavior |
| --- | --- | --- | --- | --- |
| Read assigned configuration by family | Current record IDs, revisions, registered firmware and legacy family review; minimal read-only lookup with paging | PostgreSQL verifies scoped choices, exact source IDs and independent assignment permissions | Not a wire operation | Not applicable |
| Choose multiple published records | Stage separate sources, leave unselected rows unchanged, explicit optional clear and reviewed result | Multi-source planner merges matching dependencies, rejects conflicting dependencies, clones one resulting published set, retains original source sets and other phones; stale/concurrent confirmation verified | A separately requested partial batch carries both selected changed records and the required dependency, receives their MT14s on the original batch | Stock firmware behavior pending |
| Start initial assignment and choose MT21 identity | Missing families shown as advisory; explicit registered identity opt-in | PostgreSQL verifies that only the five MT21 identity bytes change, original source retained, prior pointer null | No download implied by assigning | Physical INSTALL and subsequent connection pending |
| Optional removal and rollback | Clear only on the independently verified optional-record profile; existing rollback retained | Removing an assignment does not erase already stored terminal data; no unrelated records or schedules are changed | Explicit subsequent delivery only | Physical optional removal remains pending |
