# Modem detector timing review

The native MT33 editor has five detector groups, including answer tone and carrier/mark. Their interval, assertion and release counts are independent packed fields. The review shows all five groups together, reflects unsaved edits and opens the exact section for modification, without changing any detector policy automatically.

On one later terminal, an interval count of 1 was observed as 10 ms sampling. An answer release count of 5 therefore gives a nominal 50 ms window, and a count of 25 gives 250 ms. These are configuration calculations, not measured line behavior or a recommended default. The review omits duration estimates on earlier profiles and for zero or unknown counts. Actual detector assertion, release, mark acquisition and modem carrier must be verified on the target.

The answer detection and answer-removal controls remain independent of the counts. Changing only the answer release count updates byte offset 25 in the 36-byte MT33, with offset zero being the message type. This does not change the answer-required policy or other fields.

The [ITU-T V.22bis recommendation](https://www.itu.int/rec/T-REC-V.22bis-198811-I/en) specifies modem signaling and training sequences. It does not prescribe this terminal's MT33 detector configuration. A bench setting must not be presented as an established production default.

## Native installation and ordinary release

A completed graphical installation on 2026-11-26 used a 36-byte MT33 with the carrier-loss timeout switch enabled. Read-only verification of the retained published batch confirmed the exact stored MT33 SHA-256 `71e3b76505112de225f4e857b4df53136f4fb3fdc5d5b67902357481a4ccfcc3`, its enabled switch, 43 successful application acknowledgements from 43 assigned records, one terminal clear and a closed session. The host's bounded clear drain reported `peer_eof`, 183.5865 milliseconds elapsed with zero discarded bytes. The operator also confirmed completion of the panel installation. This establishes normal native release with the downloaded carrier-loss-monitoring configuration; it does not independently measure detector assertion or release at the physical interface, and does not qualify the separate code-server call.
