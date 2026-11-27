# Assigned configuration delivery review

Open a phone's **Table assignment** and expand **Review acknowledged configuration and ordinary refresh**. The view compares the current published set with a completed matched full download and its reliable later partial acknowledgements. It shows matching bytes, differences, records not previously acknowledged, and the conditional selected records including dependencies.

The view uses the same conservative policy as an ordinary automatic Craft refresh with no queued work. No changes, an incomplete or conflicting history, previously acknowledged records removed from the assignment, or a full dependency closure cause a full refresh rather than an empty or uncertain partial transfer. An unqualified target or absent firmware registration cannot establish a changed-record selection. Acknowledgement comparison is unavailable when the baseline is uncertain; it is not treated as proof that the current assignment is installed.

Mandatory INSTALL, explicit queued work, eligible scheduled work and connection history can alter the actual transfer. This is not a live terminal-memory readback, and does not prepare a batch, assign a set or request a call. Review the original download and application results separately. The preview is available to operators with assignment, table-record and download read access.
