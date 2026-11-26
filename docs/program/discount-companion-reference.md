# Time-of-day and discount companion references

The MT152 calendar preview selects a stored period, call zone and payment option using the staged schedule. When that selection references an applicable discount row, the editor now reads the matching saved MT93 row from the same set. It shows the stored signed adjustment in cents, percentage of normal charge, inactive mode or replacement tariff index. It does not transform one discount method into another.

For a replacement tariff, the editor also reads that exact saved MT73 row, including its classification, initial and additional periods and charges. The unlimited-period sentinel remains distinct from zero seconds. Both companion revisions are identified. No companion record is edited by this preview, and saved references can be refreshed explicitly.

Missing, unrecognised or incompatible companion layouts and failed requests have distinct messages. The preview requires a known discount-capable MT93 layout. No applicable schedule selection means no companion calculation is displayed.

These are configured values rather than a call quote. Installed table versions, actual call policy and terminal rounding need their own acceptance tests, especially for imported values and vintage-specific discount support.
