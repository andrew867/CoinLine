using static HostPlatform.Protocols.Tables.TableFieldEncoding;

namespace HostPlatform.Protocols.Tables;

public static partial class NativeTableLayouts
{
    private const int TariffRows = 128;
    private const int TariffHeaderLength = 40;
    private const int TariffRowLength = 9;
    private const int RateUnavailable = 3;
    private const int AreaRatingOffset = 14;
    private const int InternationalRatingOffset = 28;

    private static void InitializeTerminalTariffs(Span<byte> record)
    {
        for (var index = TariffHeaderLength; index < record.Length; index += TariffRowLength)
            record[index] = RateUnavailable;
    }

    private static NativeTableSection[] TerminalTariffs(bool later)
    {
        (int, string)[] classifications =
        [
            (0, "Host-rated intra-area toll"), (1, "Local measured service"), (2, "Fixed local charge"),
            (RateUnavailable, "Rate unavailable"), (4, "Invalid number"), (5, "Intra-area toll"), (6, "Inter-area toll"),
            (7, "Host-rated inter-area toll"), (8, "Host-rated local"),
            .. (later ? new[] { (9, "International toll"), (12, "Local data-port call") } : [])
        ];
        return
        [
            Section("effective", "Tariff effective date",
                [.. DateTimeFields("effective", 1), Field("telco", "Rate owner code", 7),
                 .. SpareBytes("spare", 8, 32)]),
            .. Rows("rate", TariffRows, i => $"Rate {i}", (i, key) =>
            {
                var offset = TariffHeaderLength + i * TariffRowLength;
                return [Select($"{key}.classification", "Rate classification", offset, classifications),
                    Field($"{key}.initialPeriod", "Initial period (seconds)", offset + 1, RatePeriod),
                    Field($"{key}.initialCharge", "Initial charge (cents)", offset + 3, Word),
                    Field($"{key}.additionalPeriod", "Additional period (seconds)", offset + 5, RatePeriod),
                    Field($"{key}.additionalCharge", "Additional charge (cents)", offset + 7, Word)];
            })
        ];
    }

    private static NativeTableSection[] InternationalRating() =>
    [
        Section("default", "International fallback", [
            Bits("noMatch", "When no destination matches", 1, 3,
                (0, "Ask CoinLine"), (1, "Use default rate"), (2, "Reject the call"), (3, "Reject the call (alternate encoding)")),
            new NativeTableField("fallback", "Default rate", 2, Maximum: 99, TariffIndexOffset: InternationalRatingOffset,
                Help: "Used when the fallback is set to Use default rate."),
            .. SpareFlags("spare.fallback", "Fallback flags", 1, 0xFC),
            SpareByte("spare.byte1", "Spare byte", 3)]),
        .. Rows("country", 200, i => $"Destination {i + 1}", (i, key) =>
            [Field($"{key}.country", "Country or destination prefix", 4 + i * 3, Word,
                help: "Enter the numeric prefix after the international access digits. Zero leaves the slot unused."),
             new NativeTableField($"{key}.rate", "Destination rate", 6 + i * 3,
                 Maximum: 100, TariffIndexOffset: InternationalRatingOffset, TariffFallbackCode: 100,
                 Help: "Code 100 uses the configured fallback action, as do larger codes in imported records.")])
    ];
}
