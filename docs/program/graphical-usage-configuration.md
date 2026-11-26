# Graphical usage collection review

Open MT131, MT132 or MT133 in the native graphical editor and expand **Review graphical statistics configuration**. The review shows the saved MT131 collection duration and the 24 possible screen positions, including each screen's main owner code and the six main key owner codes. It incorporates staged edits to the open record before saving.

MT131's duration applies to all three records. Header values stored in MT132 or MT133 are not used as substitutes. Zero days disables collection; a missing, incompatible or ambiguous MT131 means the period cannot be established from the current set. A partial set may leave other installed records unchanged.

A defined screen with a nonzero main owner and a nonzero collection duration is shown as configured for collection. In mixed screens, actual key presses are reported under the main screen and key positions; the panel's stored owner codes do not replace them. The review distinguishes undefined and unknown screen types, invalid staged values and ownerless screens. Owner codes are not counts. The operator can filter to configured screens and open the corresponding settings for the current record.

This is a configuration review, not a terminal-generated MT153 report or proof that a screen is reachable. The actual MT153 upload detail remains independent from these configured values. Native collection following real graphical key presses remains an acceptance check on a compatible terminal.
