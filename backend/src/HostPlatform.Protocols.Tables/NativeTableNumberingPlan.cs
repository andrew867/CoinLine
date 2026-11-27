using static HostPlatform.Protocols.Tables.TableFieldEncoding;

namespace HostPlatform.Protocols.Tables;

public static partial class NativeTableLayouts
{
    private static NativeTableLayout NumberingPlanLayout(bool later) =>
        new(62, NumberingPlanWire.RecordLength, later ? Later : MultiPay, [
            Section("numbering", "Digit collection and routing", [
                Toggle("terminateWhileCollecting", "Accept the terminating key while collecting extra digits", 1, 1),
                Toggle("terminateBeforeTimeout", "Accept the terminating key before the digit timeout", 1, 2),
                Select("inputTerminator", "Terminating key", 2, [(0, "None"), .. KeypadDigits]),
                Select("outputTerminator", "Terminating key to dial", 3, [(0, "None"), .. KeypadDigits]),
                Field("screenTimeout", "Screening digit timeout (10 ms ticks)", 4, Word),
                Field("planTimeout", "Numbering-plan digit timeout (10 ms ticks)", 6, Word),
                Select("coinZeroPlus", "Convert coin calls with a leading zero", 14, (0, "No"), (1, "Yes")),
                Select("spillUnlistedCarrier", "Send the calling-card number to an unlisted carrier on 0+ calls", 15, (0, "No"), (1, "Yes")),
                Select("internationalZone", "Default international carrier zone", 16, (0, "Intra-area"), (1, "Inter-area")),
                Select("multipleCoinCarriers", "Multiple coin carriers", 17, (0, "Disabled"), (1, "Enabled")),
                Select("routeNonInter", "Request host routes for non-interarea calls", 18, (0, "Disabled"), (1, "Enabled")),
                Field("storedValueDiscount", "Initial domestic stored-value discount (profile-specific)", 19, Word),
                new NativeTableField("keyCardNumber", "Desk terminal key card number", 21, CredentialBcd, 5, Maximum: 10,
                    Help: "Enter a ten-digit replacement. Leave blank to keep the stored number."),
                Field("internationalStoredValueDiscount", "Initial international stored-value discount (profile-specific)", 27, Word),
                Toggle("unrestrictedLocalSearch", "Allow a local dialing search without a protected prefix", 32, 1),
                .. (later ? SpareBytes("spare", 29, 3) : [
                    Field("answerSupervision", "Answer supervision timer (seconds)", 26),
                    Field("managerTimeout", "Manager no-answer timeout (10 ms ticks)", 30, Word),
                    SpareByte("spare.byte1", "Spare byte 1", 29)]),
                .. SpareFlags("spare.termination", "Termination features", 1, 0xFC),
                .. SpareFlags("spare.localSearch", "Local dialing search mode", 32, 0xFE)]),
            .. (later ? [] : new[] { Section("number-display", "Number display and currency", [
                new NativeTableField("currencySymbol", "Currency symbol", 345, TerminatedDisplayText, 4),
                Select("currencyVoice", "Spoken currency", 349, (0, "Generic"), (1, "Balboas"), (2, "Bolívares"),
                    (3, "Bolivianos"), (4, "Colones"), (5, "Córdobas"), (6, "Dollars"), (7, "Guaraníes"), (8, "Lempiras"),
                    (9, "Nuevos soles"), (10, "Pesos"), (11, "Quetzales"), (12, "Soles"), (13, "Sucres"), (14, "Tokens")),
                Field("currencyWidth", "Minimum display width", 350, max: 10),
                Field("currencyPrecision", "Decimal places", 351, max: 10),
                Toggle("formatCurrency", "Use custom currency format", 352, 1),
                Toggle("currencySuffix", "Show the symbol after the amount", 352, 2),
                Toggle("currencyAlignLeft", "Align the amount to the left", 352, 4),
                .. SpareFlags("spare.currency", "Currency format", 352, 0xF8)]) }),
            Section("rate-mapping", "Rate-response call classification", [..
                new[] { "Other rate responses", "Local rate (1 or 2)", "Intra-area rate (5)", "Inter-area rate (6)", "Rate response 7", "Rate response 8" }
                    .SelectMany((label, index) => new[] {
                        Bits($"response-{index}.call", $"{label} call type", 8 + index, 15, CallKinds),
                        Bits($"response-{index}.payment", $"{label} payment type", 8 + index, 112, [(0, "Unspecified"), .. PaymentKinds]) })])
        ]);
}
