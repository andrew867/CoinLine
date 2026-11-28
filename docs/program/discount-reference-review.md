# Terminal discount reference review

In a later-profile configuration, open MT93 and select one of its ten discount rates. Choose **Review references** before changing a discount. Published and draft configurations can both be reviewed with table-record read permission.

The review distinguishes two ways the terminal selects the same discount:

| Record | Selection | Displayed reference |
| --- | --- | --- |
| MT134, later card acceptance | A one-based embedded discount number. Zero disables it. | Recognized magnetic or stored-value card range position and saved number. |
| MT152, discount and holiday schedule | A zero-based discount index and a separate zone payment selection. | Day group, period, zone, saved index and coins/cards selection. |

An MT134 number of 10 and an MT152 index of 9 both refer to discount 10. Disabled or empty scheduled periods retain their stored selections where distinguishable from a cleared byte; invalid time windows are shown separately from configured periods. Configured references are not necessarily active for a particular call. Other call rules, overlapping periods, card eligibility and discount settings may affect selection.

Only saved records in the selected configuration revision are reviewed. Incompatible or duplicate records are reported separately from missing records. The earlier MT93 layout does not expose the later discount worksheet and does not use this review. The response does not expose card prefixes, validation keys, raw records or other configurations.

The configuration review also identifies out-of-range embedded card discount numbers, missing MT93 for recognized card discount references, and unrecognized indices in enabled, valid scheduled periods. These advisories do not change records or schedule deliveries.

The encoding and reference behavior are covered by unit cases, with configuration scope and permissions checked against PostgreSQL and selected-row behavior checked in the WebUI. This does not establish terminal use of an edited discount. Qualify actual card and scheduled discount application separately on each applicable terminal family.
