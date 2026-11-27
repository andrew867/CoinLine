# Technician directory and reported Craft activity

**Technicians and activity** keeps local employee or technician profiles separate from CoinLine operator login accounts. An operator with the directory's Add or Update permission can associate a profile with the identifier reported by a terminal. Enable matching with a separate random 32-byte Base64 server secret, `COINLINE_CRAFT_IDENTIFIER_KEY` (or `Security:CraftIdentifierKey`). Keep it out of source control, logs and client configuration. If it is absent, profiles and unassociated events still work; entering an identifier is disabled. A replacement key requires re-entering associations for the new key, not rewriting past activity.

For Docker Compose, add `docker/docker-compose.technicians.yml` to the existing Compose file list and supply the secret through the deployment environment. No key is included in the overlay.

The identifier is entered in a write-only field. CoinLine uses a keyed fingerprint to associate a complete native maintenance report with an enabled profile. It does not retain the entered digits in its directory, received DLOG event, normal NCC capture, audit details or recognized maintenance diagnostic frames. The new database migration sanitizes previously retained maintenance event payloads, and startup sanitizes recognized maintenance records in older diagnostic captures. No deleted digits can be recovered by rolling back the database migration. Database backups made before that cleanup should remain protected.

The terminal's configured Craft access code is a different setting. Open the Installation and Servicing record (MT31) and select **Craft credentials**. An administrator can explicitly select **Show configured access code** to view the saved configuration; that read is audited without recording the value. The display is temporary and is hidden when leaving the section or browser focus. The normal record response, desk key-card field and technician identifiers remain protected. To replace the terminal access code, edit a draft and enter the new code in the separate replacement field. Leaving it blank keeps the original packed value. Publish and assign the reviewed configuration through the normal table workflow. CoinLine does not claim the terminal is using the changed value until the matching record is acknowledged and behavior is verified. No separate, verified access-disable flag is exposed.

The matching process does not grant local maintenance access, modify terminal access settings, provide a login credential or authenticate the person holding the handset. A matched event is evidence that a terminal reported the associated identifier; it is not independent proof that the named person performed the action. Unmatched, unsupported and older reports remain visible without fabricated identities. The name and employee reference at reception are retained in an association snapshot, so subsequent personnel edits do not rewrite the history.

## Operation codes

The standard maintenance event catalogue contains:

| Code | Meaning |
| --- | --- |
| 996 | Technician entered maintenance mode |
| 997 | Coin path placed out of service |
| 998 | Card reader placed out of service |
| 999 | Terminal placed out of service |

These are reported occurrences, not terminal keypad commands, alarms or remote command requests. Other operation codes are operator-defined and may be configured in the event catalogue. A terminal maintenance report (MT6) receives the normal MT15 acknowledgement; alarm reports (MT7) and their MT16 acknowledgement remain separate. Matching does not change the dialogue or queue configuration data. Identical link retries in the same dialogue do not create extra attributed events. Repeated reports across independent connections may be genuine occurrences and are not automatically collapsed.

## Report

Filter activity by technician and reception time. The report shows the phone, reported opcode and human-readable meaning, and the association status. Select **Nearby alarms** to view up to twenty preceding alarms on the same phone within 24 hours. These are possible context, not evidence that the technician responded to or resolved the alarm. Actual resolution requires separate confirmation.

Normal diagnostic capture exports retain record order and timing while omitting protected data. The manager's known maintenance frame redaction masks the identifier and recomputes the checksum only for a previously valid frame. The masked frame is not an authentic technician identifier. Unknown, malformed or unrecognized diagnostic data cannot be assumed safe to share.
