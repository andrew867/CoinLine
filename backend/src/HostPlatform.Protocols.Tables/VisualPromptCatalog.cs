namespace HostPlatform.Protocols.Tables;

internal static class VisualPromptCatalog
{
    private const int CellCount = 20;
    private const int EarlyRows = 140;
    private const int ExpandedRows = 400;

    private static readonly string[] Softkeys =
    [
        "Card balance / Statement", "Set code / Lock", "Change code / Unlock", "Call bank / Transfer",
        "Card menu / Next", "Previous / Quit", "OK / Cancel", "Clear / Retry", "Other / All", "Card application selection"
    ];

    public static NativeTableSection[] Build(bool expanded)
    {
        var count = expanded ? ExpandedRows : EarlyRows;
        var sections = new List<NativeTableSection>();
        for (var row = 0; row < count; row++)
        {
            var message = row / 2;
            var label = row % 2 == 0 ? MessageName(message, expanded) : null;
            if (label != null)
            {
                sections.Add(new($"message-{message}", $"Message {message} · {label}",
                    [Line(row, "Upper line"), Line(row + 1, "Lower line")], ShowDisplayPreview: true));
                row++;
                continue;
            }

            var (title, help) = (expanded, row) switch
            {
                (true, >= 388 and <= 397) => ($"Softkeys · {Softkeys[397 - row]}", "Two labels of ten display cells each."),
                (true, >= 398) => ($"Month abbreviations · {(row == 398 ? "January through June" : "July through December")}",
                    "Six abbreviations of three display cells each, followed by two padding cells."),
                _ => ($"Wire row {row}", null)
            };
            sections.Add(new($"prompt-{row}", title, [Line(row, "Display text", help)]));
        }
        return sections.ToArray();
    }

    private static NativeTableField Line(int row, string label, string? help = null) =>
        new($"prompt-{row}.text", label, 1 + CellCount * row, TableFieldEncoding.DisplayText, CellCount,
            Help: help == null ? $"Wire row {row}." : $"Wire row {row}. {help}");

    private static string? MessageName(int message, bool expanded) => message switch
    {
        0 => expanded ? "Lift receiver" : "Messaging offer on supported builds",
        1 => expanded ? "Lift receiver and insert card" : "Card removal or data-port payment",
        2 => "Insert and remove card",
        3 => "Remove card",
        4 => "Insert card in slot",
        5 => "Insert card fully",
        6 => "Card orientation",
        7 => "Card not accepted",
        8 => "Expired card",
        9 => "Card read failure",
        10 => "Card validation in progress",
        11 => "Please wait",
        12 => "Invalid card",
        13 => "Card accepted, dial number",
        14 => "Dial number",
        15 => "Correct entered digits",
        16 => "Calling",
        17 => "Enter PIN",
        18 => "Dial next call",
        19 => "Enter calling-card number or wait",
        20 => "Unprogrammed key",
        21 => "Press next-call key",
        22 => "Next-call instructions",
        23 => "Free call",
        24 => "Replace receiver and retry",
        25 => "Replace receiver",
        26 => "Next-call restriction",
        27 => "Number access denied",
        28 => expanded ? "Out of service" : "Emergency service",
        29 => "Card service only",
        30 => "Coin service or stored-value deposit",
        31 => "Ringing",
        32 => "Restart in progress",
        33 => "Invalid telephone number",
        34 => "Telephone hardware unavailable",
        35 => "Alarm sent",
        36 => "Insert card or dial number",
        37 => "Insert card or deposit coins",
        38 => "Deposit coins",
        39 => "Deposit credit",
        40 => "Dial next call with credit",
        41 => "Call ending, continue",
        42 => "Remaining call time",
        43 => "Insert card or wait for rating",
        44 => "Call cannot be completed",
        45 => "No further coin deposits",
        46 => "Carrier message",
        47 => "Card invalid for carrier",
        48 => "Rating unavailable",
        49 => "Card validation unavailable",
        50 => "Charge limit exceeded",
        >= 51 and <= 55 when expanded => $"Card authorization result {message - 50}",
        >= 51 and <= 55 => "Unassigned in this profile",
        56 => "Local dialing table unavailable",
        57 => "Insert stored-value card or dial",
        58 => "Insert stored-value card in slot",
        59 => "Check card orientation",
        60 => "Insert stored-value card fully",
        61 => "Stored-value card or coin deposit",
        62 => "Stored-value card or wait for rating",
        63 => "Insufficient funds",
        64 => "Dial number using credit",
        65 => "Insert another stored-value card",
        66 => "Replace receiver to retain credit",
        67 => "Show call rate",
        68 => "Unsupported payment type",
        69 => "Limited service level",
        _ when !expanded => null,
        70 => "Emergency service",
        71 => "Credit remaining for current call",
        72 => "Data port connecting",
        73 => "Data port connection lost",
        74 => "Replace receiver for data port",
        75 => "Data port available",
        76 => "Insert card for data-port charge",
        77 => "Lift receiver for off-hook use",
        171 => "Optional rate display",
        172 => "Enter service ID",
        173 => "Invalid service telephone number",
        174 => "Data port unavailable",
        175 => "Check data-port connection",
        176 => "Rate quote",
        177 => "Open help menu",
        178 => "Select data or voice help",
        179 => "Follow help instructions",
        180 => "Connect data device",
        181 => "Prepare data device",
        182 => "Dial with data device",
        183 => "Lift receiver to use credit",
        184 => "Redial in the required format",
        185 => "Enter card expiry",
        186 => "Enter credit card number",
        187 => "Cash box collection",
        188 => "Use change card",
        189 => "Debit in progress",
        190 => "Change card empty",
        _ => null
    };
}
