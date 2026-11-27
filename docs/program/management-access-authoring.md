# Authoring a new management-access record

MT21 establishes a terminal's on-wire identity and the primary and alternate manager dial numbers. It is site-specific configuration, not a shared demonstration default. The native editor can author it directly instead of requiring an imported binary.

1. Open an unpublished table set, including a new guided setup draft, and choose **Terminal features and call setup**, then **NCC terminal access**.
2. If no MT21 exists, choose a supported layout. A set with a confirmed target offers only its matching format; an untargeted draft requires an explicit choice. The 18-byte profile has two 12-digit manager fields. The 48-byte profile has two 20-digit fields and optional four-byte ADSI identifiers.
3. Enter the actual ten-digit terminal wire ID and primary manager number. Enter the alternate number when applicable. On the later format, enter the CAD and CPE identifiers as eight hexadecimal digits each when the site requires them.
4. Create the record. Review the saved native fields and the configuration set before publication. Creating a record does not assign the set, register or rename a phone, schedule a download, or establish a working management or ADSI connection.

The alternate manager number may be empty. Unspecified optional ADSI identifiers and unassigned trailing bytes in a newly authored record start at zero. No site ID or manager numbers are assumed. The partially mapped 40-byte import remains import-only; a target with only observed record lengths also requires an independently reviewed matching record.

The initializer requires existing record-add permission and refuses published sets, duplicate records, mismatched target layouts and missing or malformed required values. The ordinary native editor and existing assignment identity checks still apply. Choose a real, compatible site configuration before publishing and confirm terminal acknowledgements and subsequent service independently.

Isolated PostgreSQL tests authored both complete formats, checked the exact encoded bytes and reloaded the later native fields. An unchanged save retained the digest and revision; an alternate-number edit changed only the intended digit nibble and preserved the ADSI identifiers and tail. The tests confirmed no phone assignment or download was created. Browser tests verified required inputs, read-only access, profile selection and preservation of entered values after a failed create. These are authoring and encoding results, not new terminal acknowledgement or service evidence.
