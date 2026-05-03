# Compatibility validation items

Items that may require **device validation** or **field validation** before a customer treats behavior as certified for their fleet. This list is the public successor to internal engineering tracking; it is **not** a warranty statement.

| ID | Area | Why it matters | Current product posture | Proposed customer validation |
|----|------|----------------|-------------------------|------------------------------|
| CV-001 | Host–terminal transport | Live DLOG and download behavior depend on modem and line quality | Exercised in lab; production timing varies | Capture traces on your lines; compare to [Field validation](../field-validation/overview.md) |
| CV-002 | Table download completion | Host records download intent; terminal completion may need confirmation | End-to-end status varies by terminal | Observe terminal and host during controlled download test |
| CV-003 | Set/table-rated call rating | Rating uses table and prefix configuration | MVP rules; unknown prefixes may be denied | Run [UAT-style scenarios](../field-validation/rated-call.md) on your hardware |
| CV-004 | OpenAPI static export | Committed JSON may omit some minimal health routes | Use live `/swagger` for probes | Add integration tests in your environment for `/ready` and `/metrics` if required |
| CV-005 | Payment card scope | Card APIs are payment-adjacent | Simulation defaults; token discipline in product | PCI scoping workshop per deployment |

Add rows as your program discovers deployment-specific gaps. Close items with evidence (test report, field sign-off, or product change in a [Changelog](../release/changelog.md) entry).
