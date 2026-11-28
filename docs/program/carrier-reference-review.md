# Review carrier references

The carrier number stored in MT23 or MT135 is an identifier, not the position of the carrier row in the table. Before replacing or renumbering an existing carrier, select its row and choose **Review carrier references**. Both earlier and later verified Multi-pay layouts support the review. The unused identifier 255 is not a selectable carrier.

The review lists the saved carrier rows matching the identifier and their separate card-acceptance bitmaps, then shows the following related settings:

- Default primary, coin and credit-card selectors, including local and intra-area selectors on later profiles. The saved terminal operating mode distinguishes some retained payment-specific settings.
- Card acceptance range positions in MT22 or MT134 selecting the identifier. Unknown standards and retained selections are listed separately from identified configured settings.
- MT92 screening rules and MT55 quick-access keys using the free Feature Group B selection. A paid tariff field of the same numerical value is not a carrier pointer. A zero Feature Group B selection uses the applicable default rather than explicitly selecting carrier zero.

All ten stored quick-access positions are reviewed. Five-key terminals may not use every position. No card prefixes, destination digits, display names, raw records or credentials are returned. Duplicate identifiers, invalid companion layouts and missing records are made explicit. Missing records might already be installed independently on the phone.

The inspection uses the saved identifier and configuration revision even while a new identifier is being edited. It does not rewrite other records or change the assigned configuration. Review any affected default, card or call-routing setting explicitly before publication. Carrier acceptance bits and configured references do not confirm successful routing or payment on a particular terminal.

Protocol unit tests cover both packed profiles, distinct carrier row identifiers, default and explicit references, and independent duplicate-row bitmaps. PostgreSQL and WebUI coverage verify permissions, configuration isolation, byte preservation, and the saved-versus-staged review. Actual behavior after a carrier change requires separately observed terminal acceptance and calls.
