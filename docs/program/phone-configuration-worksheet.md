# Individual phone configuration assignment parity

The Manager terminal information workflow includes individual configuration families and read-only lookup by configuration ID. CoinLine provides the phone-centric worksheet alongside whole-set assignment and the existing group/region selected-record rollouts.

| Capability | UI and API | Encoding and persistence | NCC acknowledgement | Terminal behavior |
| --- | --- | --- | --- | --- |
| Read assigned configuration by family | Current record IDs, revisions, registered firmware and legacy family review; minimal read-only lookup with paging | PostgreSQL verifies scoped choices, exact source IDs and independent assignment permissions | Not a wire operation | Not applicable |
| Choose multiple published records | Stage separate sources, leave unselected rows unchanged, explicit optional clear and reviewed result | Multi-source planner merges matching dependencies, rejects conflicting dependencies, clones one resulting published set, retains original source sets and other phones; stale/concurrent confirmation verified | A separately requested partial batch carries both selected changed records and the required dependency, receives their MT14s on the original batch | Stock firmware behavior pending |
| Start initial assignment and choose MT21 identity | Missing families shown as advisory; explicit registered identity opt-in | PostgreSQL verifies that only the five MT21 identity bytes change, original source retained, prior pointer null | No download implied by assigning | Physical INSTALL and subsequent connection pending |
| Optional removal and rollback | Clear only on the independently verified optional-record profile; existing rollback retained | Removing an assignment does not erase already stored terminal data; no unrelated records or schedules are changed | Explicit subsequent delivery only | Physical optional removal remains pending |

## Start an unassigned phone from a published configuration

Register the phone with its actual firmware, site and terminal family. In its **Individual configuration assignments** worksheet choose **Start from a published configuration**. The starter lookup lists data-only published sets targeted to the same registered firmware, for the phone's customer or shared. It does not offer unpublished examples, another customer's configurations, or code schedules. A length-only observed profile cannot qualify a whole starter; choose individual records after reviewing that profile instead.

Review the selected source's exact record list and the missing required families, then stage all compatible records in one action. A starter with a mismatched MT21 requires explicit selection of the phone's registered wire ID before the preview can succeed. A missing MT21 remains visible as a required review item. Adjust or remove individual staged choices if needed; the copied source retains the original access settings, routes, payment options, tariffs, advertisements and unrelated bytes. Review these settings for the phone's actual site before applying the assignment.

The existing worksheet preview and confirmation create a new immutable published assignment, not an automatic download; the source stays unchanged. The operator must separately prepare a download and inspect its acknowledgements. Missing capabilities, site-specific configuration and device behavior are not certified by the starter lookup.
