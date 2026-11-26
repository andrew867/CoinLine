# Card-present audio alert controls

The MT26 Feature Options editor groups six stored timing values under **Card-present audio alert** for both supported 72-byte Multi-Pay layouts.

| Setting | Record byte offset |
| --- | ---: |
| On-hook card-present audio alarm delay | 55 |
| Post-call card-present audio alarm delay | 57 |
| Card-present audio alarm timing | 59 |
| Audio alarm cadence on timer | 61 |
| Audio alarm cadence off timer | 63 |
| Card-reader blocked alarm delay | 65 |

Offsets are zero-based, including the message-type byte. All six fields are unsigned 16-bit stored values. The editor preserves each unchanged field and the surrounding record, including the distinct fraud-control alarm option in Reporting and supervision.

Time units, zero-value behavior, and the audible reminder have not been qualified on each terminal profile. A forgotten-card display prompt and its dismissal were observed on a graphical terminal, but that observation does not establish audible warning operation. Retain the installed timing values until the intended behavior is verified. No separate ordinary card-present alarm enable control is currently established.
