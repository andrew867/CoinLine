# Firmware upgrade configuration review

Firmware jobs includes a read-only configuration transition review. Select a phone, a registered target firmware version and, optionally, an independently prepared configuration set explicitly targeted to that version. The review compares the phone's assigned record lengths with the target profile and the supplied target records. It neither changes an assignment nor requests a device action. Operators need read access to firmware jobs, the firmware registry, phones, table sets, table records and assignments because the review combines those screens' data.

The table identifies records that need replacement, missing source features, same-length fields that need layout confirmation, new target records and known target records whose applicability has not been established. A missing record type in an incomplete catalogue is unknown, not proof that the feature was removed. A catalogue of acknowledged records is not a list of mandatory records.

## Example: MTR 2.0 to MTR 2.11

The retained configuration evidence establishes these differences between the earlier and later Multi-pay profiles. The earlier profile's recorded INSTALL did not reach service. Neither profile alone establishes a compatible remote image transition.

| Record | Earlier MTR 2.0 length | MTR 2.11 class length | Configuration review |
| --- | ---: | ---: | --- |
| MT21 management access | 18 | 48 | Target-specific record required, including independently reviewed new fields. |
| MT26 feature options | 72 | 72 | Same length; field layout must be independently confirmed. |
| MT92 screening | 3,401 | 3,061 | Review the 200 to 180 entry change, order, and any entries exceeding target capacity. Do not truncate. |
| MT22 and MT23 earlier card and carrier data | 661 and 678 | Unknown for this target profile | Review the target card and carrier records and preserve intended acceptance and routing. |
| MT134 and MT135 later card and carrier data | Unknown for the earlier profile | 1,153 and 1,109 | Prepare target data; a length match does not prove card or route behavior. |
| MT85 and MT86 later visual prompts | Unknown for the earlier profile | 8,001 each | Confirm whether applicable and prepare target records. |

Source and target configuration should remain separate. Do not activate target records against the installed source profile, or edit the phone's registered firmware simply to enable a download.

The reviewed MT73 carry-forward stages the common earlier classifications and signed period/unsigned charge tuples into a separate later candidate. It groups equal source rows, reuses exact existing target tuples, and proposes new allocations only in canonical empty invalid/unavailable positions. The candidate's local index domain 0 through 3 and the numeric tariff references in included screening, quick-access, later graphic, stored-value discount, area, international and local-exchange records remain protected. Other active or noncanonical target tuples are not overwritten. Unreviewed included records prevent allocation; absent companion records remain explicitly unknown. The preview identifies the source-to-target mapping to use in subsequent configuration migration; candidate header, date and all other records remain unchanged. Actual rating behavior is not established by a matching tuple or draft.

## Qualification sequence

1. Identify the exact installed hardware family, installed version, target version and approved compatible image. Record the source assignment and effective records, including protected per-phone settings without recording their values in public evidence.
2. Prepare a separate candidate configuration using proven target layouts and deliberate conversion rules. Validate required records, preserve supported site settings and verify each intended change. Resolve outstanding downloads before scheduling.
3. Qualify the native scheduled call, independent code-server connection, carrier, protocol exchange and image integrity with an isolated test terminal. Test interrupted transfer and recovery before permitting an upgrade on an operational terminal.
4. After the upgrade, verify the device actually reports the target version before applying the prepared configuration. Record the exact selected download and acknowledgements; neither a scheduled job nor an acknowledged code schedule proves an installed version change.
5. Verify normal warm service, free and rated calls, relevant payment and feature behavior, routine reports and recovery. Repeat on physical equipment before representing the transition as hardware-qualified.

The management-access carry-forward prepares a separate target draft using the source's registered identity and canonical manager numbers in the identified 18- or 48-byte MT21 layout. The target candidate supplies its own ADSI identifiers and reserved data. The conversion rejects noncanonical terminated numbers, missing primary access, mismatched identity, unqualified lengths and values too large for the shorter layout. The preview returns counts and a revision-bound token, not access values. No other record is converted. The remaining record migration, automatic activation staging, actual code delivery, post-reboot target-version confirmation, and service qualification remain separate work. The earlier observed code-server connection exchanged no application bytes and did not change flash. No upgrade compatibility pair has been qualified by this review.
