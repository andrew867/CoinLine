# CoinLine operator guide: MT93 and MT134

## Start here

There are two different jobs involved in configuring a payphone to use cards. **MT134 tells the terminal which cards it recognizes and how it should handle them. MT93 tells the terminal which stored-value limits and discounts are available.** Neither table creates a bank account or puts money on a card.

If you only want an ordinary magnetic-stripe test card to pay for a call through an existing host payment provider, start with the card acceptance and carrier configuration. You generally do not need to change MT93 unless that card explicitly uses an embedded discount.

| Terminal | Card acceptance | Carrier configuration | Smart-card parameters |
| --- | --- | --- | --- |
| Earlier MTR 1.9 Multi-pay | MT22 | MT23 | MT93, earlier layout |
| Verified later MTR 2.x Multi-pay and graphical Multi-pay | MT134 | MT135 | MT93, later layout |

MT93 is 225 bytes on both layouts, but the same length does not mean the fields have the same meaning. Choose the layout for the actual terminal. A graphical later-profile phone uses the later editor, not the earlier rebate and face-value worksheet. Other terminal variants may require separate qualification.

## Where to find the editors

1. Open **Table sets** and choose the configuration you want to work on.
2. If it is published, create a separate draft copy before editing. Keep the currently assigned set intact until the new one has been reviewed.
3. Open **Cards, coin and carriers** in the configuration editor.
4. Select **MT93 · Smart-card parameters** or **MT134 · Card acceptance**. For an earlier phone, select **MT22 · Card acceptance, older** instead of MT134.
5. If the editor asks for a layout, choose the one supported by the terminal's registered version. Do not select a layout merely because both records have the same size.
6. Use **Section or entry** to move between rows. Changes are staged in the editor until you choose **Save record**.

An existing validated record is usually a better starting point than a new blank table. In particular, do not replace an existing MT93 just to change a price or a limit.

## MT93: stored-value limits and discounts

### Earlier MTR 1.9 layout

| Section or field | What it controls | What to enter |
| --- | --- | --- |
| Stored-value toll rebates | Domestic and international toll rebates, in cents | The desired rebate amounts supported by the terminal's configured stored-value policy. |
| Large-value card limits | Maximum face value for each of 16 card-value indexes | Values from the authorized card policy. These are encoded limits, not an account balance. Use the same unit convention as that policy. |
| Stored-value unit index | 15 independent entries, numbered 0 to 14 | The maximum allowed units for the selected index and its multiplier. |
| Unit multiplier | An encoded multiplier of 1, 5, 10 or 25 | The multiplier specified for that unit index by the applicable card policy. Do not guess a currency conversion. |
| Encoded validation keys | Write-only credential slots | Leave unchanged unless managing an authorized credential replacement. |
| Reserved configuration | Additional retained fields | Leave untouched unless a field's meaning and intended use are established. |

The unit index is a reference into a table of limits. It is not a dollar amount, and changing it does not change the balance reported by a payment provider.

### Later MTR 2.x and graphical layout

The later editor contains ten **Discount rate** rows, numbered 1 through 10, followed by the unit-index and protected sections. Each discount row has four call zones: Local, Intra-area, Inter-area and International.

Every zone has its own **discount method**. Choose the method before entering its values because changing the method clears the old values for that zone only.

| Discount method | Initial field | Additional field |
| --- | --- | --- |
| Adjust charge in cents | Signed adjustment to the initial charge | Signed adjustment to the additional charge |
| Percent of normal charge | Percentage of the normal initial charge | Percentage of the normal additional charge |
| Replacement tariff | An existing MT73 tariff index, 0 to 127 | Not used as a second tariff selection |
| Disabled | No applicable discount for this zone | No applicable discount for this zone |

For percentages, **80 means charge 80% of the normal price**, not take 80% off the price. A negative cents adjustment reduces the charge; a positive adjustment increases it. Verify the actual rounding and charging behavior with the intended terminal rather than assuming it from the worksheet.

The other sections have the same basic purpose as above: fifteen stored-value unit indexes, their multipliers, protected validation fields and reserved configuration.

### Worked example: a simple discount

Suppose your existing authorized test configuration has a local initial tariff of 100 cents. You want a discount that charges 80% of its normal local price.

1. Open later MT93 and select **Discount rate 1**.
2. For the Local zone, select **Percent of normal charge**.
3. Set the initial percentage to `80` and the additional percentage to `80` if that is the intended ongoing policy. Leave the other zones unchanged or disabled as appropriate.
4. Save MT93.
5. In the applicable, already configured later MT134 test-card range, set **Embedded discount index** to `1` if that card policy is intended to use this discount. Save MT134 if it changed.

The intended initial calculation is 100 cents × 80% = 80 cents. This is an example of configuring a price rule, not evidence that a particular card or terminal has applied it. Observe a controlled test call to verify the result.

If the discount is instead selected by the time-of-day schedule, use **MT152** to choose the intended MT93 discount and payment type for the applicable period. The MT152 editor displays human-readable discount names even though the underlying schedule index is zero-based. MT134 uses the human-facing one-based discount number; `0` disables its embedded discount.

When available in your installed Manager version, **Review references** on an MT93 discount row shows the saved MT134 card ranges and MT152 periods selecting that row. It deliberately does not include unsaved edits or prove actual call selection.

## MT134: later card acceptance

MT134 contains 32 card range positions. One range can describe an authorized family of cards; it does not represent an individual bank account. For earlier MTR 1.9, the corresponding MT22 editor has 20 positions and some different fields.

### Main card range section

| Field | Plain-language meaning | What to enter |
| --- | --- | --- |
| Lowest six-digit prefix / Highest six-digit prefix | The range of card-number prefixes this entry is intended to recognize | The authorized range supplied for your actual or lab card policy. For an exact test range, use the same approved prefix at both ends. Do not enter a complete card number. |
| Card number standard | The card format/application family | The standard established for that card and terminal. A magnetic format, disposable stored-value application and a supported contact-card application are different things. |
| Expiry, start-date and discretionary-data positions | Where the applicable format puts particular fields | Use the format-specific documentation for an existing card policy. Do not guess from another card. |
| Card reference number | An identifier used by the terminal's card policy | Keep the existing reviewed reference or use the one specified for the policy. It is not a PAN or the range position. |
| Carrier reference | Which carrier identifier the range selects | An existing reviewed MT135 carrier reference. It is not the row number in MT135. The value 255 means no carrier is selected here. |
| Require host validation / Use credit-card processing | Whether the configured card path uses host validation and credit processing | Match the established payment-provider configuration. Do not disable validation merely to make a declined test call proceed. |
| Delayed authorization and PIN/carrier prompts | When the selected carrier/payment workflow asks for additional information | Retain the reviewed settings unless the same workflow requires a change. |
| Unformatted authorization and calling-card routing | Special request and carrier workflows | Use only when required by the configured, authorized workflow. These are not needed simply to enter a normal discount. |

Changing the card **family** can clear the old service-code or stored-value data for that position because the same space has a different meaning. Review the newly displayed sections before saving, rather than switching a working range experimentally.

The magnetic choices include Mod 10, ANSI, ABA and other named formats. The later stored-value choices include the generic stored-value card, Mondex or desk (profile-dependent), SmartCity GPM416, PCOS and MPCOS, Proton, and Visa or CT (profile-dependent). These are choices presented by the editor, not a list of applications guaranteed to work on every terminal. Select the one supported by the actual authorized card policy.

### Magnetic-stripe card range

The **service codes** section holds the allowed three-digit service-code entries for the selected range. The later layout supports five entries, while the earlier layout supports ten. A blank entry ends the list; enter any approved list without gaps. The main card range has a separate **Accept listed service codes only** option.

The later **data spill** section is a specialized calling-card carrier workflow. It is not necessary for an ordinary host-authorized magnetic payment merely because the section appears. Leave a working spill sequence unchanged unless the corresponding carrier workflow and data handling have been independently reviewed. It may have protected information in its transmission sequence.

The **Embedded discount index** in the later data-spill section selects MT93 discount 1 through 10 for that applicable card range, or zero for no embedded discount.

### Stored-value card range

When a recognized stored-value application is selected, MT134 exposes additional sections:

| Section or field | Meaning |
| --- | --- |
| Embedded discount index | Selects MT93 discount 1 through 10, or zero for disabled. |
| Reloadable stored-value card | Indicates the intended reload policy for the supported application. It does not create a reload service. |
| Require a summary card | A requirement used by the configured application policy. |
| Display remaining balance | Whether the terminal should display a balance when the application provides it. |
| Use only for magnetic-card reload | Restricts this range to the relevant reload use. It is not a general payment-acceptance setting. |
| Reload call-screening entry | A one-based MT92 entry selecting the intended reload route; zero uses the terminal's entry-1 default. Review that route before relying on the fallback. |
| Issuer validation | The authorized application-specific issuer associations. Keep existing values unless separately authorized to maintain that credential relationship. |
| Value and manufacturer checks | Maps the card application's value codes to the fifteen MT93 unit indexes (0 through 14) and lists applicable manufacturer checks. The value 15 in a unit-index selection means reject that value code, not unit index 15. The editor validates supported index relationships. Use the application's documented values, not invented codes. |

**Do not confuse generic contact-reader detection with successful stored-value payment.** A terminal can recognize that a card is physically present and communicate with it without supporting the application's approval, balance or settlement operations. The selected standard in MT134 is a configuration choice, not proof of payment support.

## The credential fields are different from ordinary settings

MT93 validation credential fields are deliberately write-only in CoinLine. The editor does not display the saved bytes. An empty input means **leave the existing value unchanged**, not clear it. You do not need to re-enter credentials to change a rebate, unit limit or discount.

MT134 issuer associations must correspond to the authorized application policy. Do not derive issuer values, substitute arbitrary credentials or copy a credential configuration from a different card application. If the applicable issuer-provided values are unavailable, leave the protected settings unchanged and do not count that application as qualified.

The ordinary-field save regression has been verified in both MT93 layouts using noncredential synthetic sentinel bytes, including persistence in PostgreSQL and selected-record loopback NCC delivery. All opaque bytes were retained. This does not establish that any particular historical card credential or stored-value application is valid.

## Check the other tables before blaming the card

| Record or service | Why it matters |
| --- | --- |
| MT26, feature options | The terminal's configured payment mode must allow the intended card or coin path. |
| MT135 / MT23, carrier configuration | The applicable carrier identifier and its acceptance for the selected card range must agree with the card record. |
| MT62, numbering plan | Determines how the terminal interprets dialed numbers and the payment class associated with a route. |
| MT92, call-screening list | Determines whether a destination is allowed, free, rated or restricted to a particular payment path. |
| MT73, terminal rate table | Provides ordinary tariffs and any replacement tariff referenced by MT93. |
| MT152, time-of-day discounts | May select a different discount based on the period, call zone and payment method. |
| CoinLine payment provider | Determines actual host authorization and any eventual settlement. Its account balance is separate from the terminal tables. |

If the terminal says **No access to that number**, inspect the numbering plan and screening rules first. If it says **Selected carrier not supported by card**, inspect the card range and selected carrier's acceptance. Neither symptom proves there is a problem with MT93. If the terminal requests host validation but is declined, inspect the redacted payment decision in CoinLine rather than changing the configured validation policy at random.

## Save, download and test

1. Save one changed record, reopen it, and confirm the intended values. Leave any blank protected input blank. If you made no changes, the saved record's digest should be unchanged.
2. Review the related records listed above. When available, use the discount and carrier reference reviews before replacing a shared identifier.
3. Publish the reviewed draft and assign it only to the intended phone or test group. Keep the preceding version available for rollback.
4. Prepare a selected download for MT93 and/or MT134 if those are the records that actually changed, including any genuinely changed dependency. An initial INSTALL may require the complete applicable configuration instead.
5. Let the terminal pick up the download through its normal management workflow. Check that the original batch records the expected MT14 acknowledgements and normal completion.
6. In a controlled lab, use the already authorized test instrument and a local test route. Confirm the displayed price, card acceptance or decline, host authorization outcome, call result and uploaded record separately. A successful table download alone does not establish card payment or settlement behavior.

This guide covers the verified editor layouts and ordinary configuration workflow. It intentionally does not supply payment credentials, card personalization instructions or historical issuer-specific parameters.
