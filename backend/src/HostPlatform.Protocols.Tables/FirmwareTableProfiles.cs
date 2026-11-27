namespace HostPlatform.Protocols.Tables;

public sealed record FirmwareTableProfile(
    string Id, string Label, string? NativeLayout, IReadOnlyDictionary<int, int> VerifiedLengths,
    IReadOnlySet<int> TerminalAcknowledgedRecords, bool CompleteCatalogue,
    IReadOnlySet<int> SharedLayouts, string? EvidenceNote = null)
{
    public bool ObservedLengthsOnly => NativeLayout == null;
    public string? LayoutFor(int number) => SharedLayouts.Contains(number) ? NativeTableLayouts.MultiPay : NativeLayout;
}

public static class FirmwareTableProfiles
{
    public const string MultiPay200 = "mtr19-multipay-200";
    public const string LaterMultiPay180 = "mtr2x-multipay-180-double-compressed";
    public const string GraphicalMultiPay180 = "mtr2x-multipay-graphical-180-double-compressed";
    public const string Msr17MultiPay = "msr17-multipay-compact-uncompressed";
    public const string Msr17Card = "msr17-card-compact-uncompressed";
    public const string Msr17Desk = "msr17-desk-compact-uncompressed";
    public const string Msr17CoinBasic = "msr17-coin-basic-compact-uncompressed";
    public const string Mtr19Desk = "mtr19-desk-200-single-compressed";
    public const string Mtr113MultiPay = "mtr113-multipay-200-single-compressed";
    public const string Mtr120MultiPay = "mtr120-multipay-180-double-compressed";
    public const string Mtr120CoinBasic = "mtr120-coin-basic-180-double-compressed";
    public const string Mtr20EarlyMultiPay = "mtr20-early-multipay-200-single-compressed";
    public const string Mtr211MultiPay = "mtr211-multipay-180-double-compressed";

    private const string ColdInstallEvidence = "The retained lab set was acknowledged during a successful emulated cold INSTALL. Other record lengths, native field meanings and physical hardware remain unverified.";

    public static IReadOnlyList<FirmwareTableProfile> Catalog { get; } =
    [
        new(MultiPay200, "MTR 1.9 Multi-pay, 200-entry screening", NativeTableLayouts.MultiPay,
            ReadOnly(new Dictionary<int, int>
            {
                [21] = 18, [22] = 661, [23] = 678, [26] = 72, [27] = 2801, [28] = 2801,
                [29] = AdvertisingTableWire.RecordLength, [30] = 68, [31] = 37, [32] = 33,
                [33] = 36, [34] = 19, [35] = 21, [50] = 105, [55] = 571, [58] = 26,
                [62] = 353, [68] = 72, [73] = 1192, [92] = 3401, [93] = 225,
                [101] = 403, [102] = 403, [103] = 403, [104] = 403, [105] = 403, [106] = 403, [107] = 403
            }), new HashSet<int> { 21, 22, 23, 26, 29, 31, 35, 50, 62, 73, 92, 93, 101 },
            false, new HashSet<int>()),
        new(LaterMultiPay180, "MTR 2.x Multi-pay, 180-entry, double-compressed LCD",
            NativeTableLayouts.Later, LaterCatalogue(),
            new HashSet<int> { 21, 26, 31, 50, 62, 72, 73, 92, 93, 134, 135, 136 }, true,
            new HashSet<int> { 30, 32, 35, 50 }),
        new(GraphicalMultiPay180, "MTR 2.12-based graphical Multi-pay (type 40), 180-entry, double-compressed LCD",
            NativeTableLayouts.Later, LaterCatalogue(graphical: true),
            new HashSet<int> { 21, 26, 29, 30, 31, 32, 33, 34, 35, 49, 50, 55, 62, 72, 73, 85, 86, 92, 93,
                131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149,
                150, 151, 152, 154, 155 }, true,
            new HashSet<int> { 30, 32, 35, 50 },
            "A complete 43-record emulated configuration transfer received all application acknowledgements at the registered lengths and cleared normally. MT58 and MT94 were not included in this batch. Feature behavior and physical acceptance remain independent."),
        Observed(Msr17MultiPay, "MSR 1.7-family Multi-pay, compact screening and uncompressed LCD",
            EarlyObserved(cards: true, coins: true)),
        Observed(Msr17Card, "MSR 1.7-family Card-only, compact screening and uncompressed LCD",
            EarlyObserved(cards: true, coins: false)),
        Observed(Msr17Desk, "MSR 1.7 Desk, compact screening and uncompressed LCD",
            EarlyObserved(cards: true, coins: false)),
        Observed(Msr17CoinBasic, "MSR 1.7 Coin Basic, compact screening and uncompressed LCD",
            EarlyObserved(cards: false, coins: true)),
        Observed(Mtr19Desk, "MTR 1.9 Desk, 200-entry screening and single-compressed LCD",
            SingleCompressedObserved(coins: false)),
        Observed(Mtr113MultiPay, "MTR 1.13 Multi-pay/TTY, 200-entry screening and single-compressed LCD",
            SingleCompressedObserved(coins: true)),
        Observed(Mtr120MultiPay, "MTR 1.20 Multi-pay, 180-entry screening and double-compressed LCD",
            DoubleCompressedObserved(cards: true, spare: false, prompts: false)),
        Observed(Mtr120CoinBasic, "MTR 1.20 Coin Basic, 180-entry screening and double-compressed LCD",
            DoubleCompressedObserved(cards: false, spare: false, prompts: false)),
        Observed(Mtr20EarlyMultiPay, "Early MTR 2.0 Multi-pay, 200-entry screening and single-compressed LCD",
            SingleCompressedObserved(coins: true),
            "All listed records received MT14 and the host marked the batch completed. A recorded cold INSTALL still ended with a firmware error; service behavior is not established."),
        Observed(Mtr211MultiPay, "MTR 2.11-class Multi-pay, 180-entry screening and double-compressed LCD",
            DoubleCompressedObserved(cards: true, spare: true, prompts: true))
    ];

    public static FirmwareTableProfile? Find(string? id) => Catalog.SingleOrDefault(profile => profile.Id == id);

    private static FirmwareTableProfile Observed(string id, string label, Dictionary<int, int> lengths,
        string? evidence = null) => new(id, label, null, ReadOnly(lengths), new HashSet<int>(lengths.Keys),
            false, new HashSet<int>(), evidence ?? ColdInstallEvidence);

    private static Dictionary<int, int> EarlyObserved(bool cards, bool coins)
    {
        var lengths = new Dictionary<int, int>
        {
            [21] = 18, [23] = 678, [24] = 721, [26] = 72, [31] = 37,
            [62] = 353, [73] = 1192, [74] = 819
        };
        if (cards) lengths.Add(22, 661);
        if (coins) lengths.Add(50, 105);
        return lengths;
    }

    private static Dictionary<int, int> SingleCompressedObserved(bool coins)
    {
        var lengths = new Dictionary<int, int>
        {
            [21] = 18, [22] = 661, [23] = 678, [26] = 72, [31] = 37,
            [62] = 353, [73] = 1192, [92] = 3401, [93] = 225, [101] = 403
        };
        if (coins) lengths.Add(50, 105);
        return lengths;
    }

    private static Dictionary<int, int> DoubleCompressedObserved(bool cards, bool spare, bool prompts)
    {
        var lengths = new Dictionary<int, int>
        {
            [21] = 48, [26] = 72, [31] = 37, [50] = 105, [62] = 353, [73] = 1192,
            [92] = 3061, [93] = 225, [135] = 1109, [136] = 203
        };
        if (cards) lengths.Add(134, 1153);
        if (spare) lengths.Add(72, 1001);
        if (prompts)
        {
            lengths.Add(85, 8001);
            lengths.Add(86, 8001);
        }
        return lengths;
    }

    private static IReadOnlyDictionary<int, int> LaterCatalogue(bool graphical = false)
    {
        var records = new Dictionary<int, int>
        {
            [21] = 48, [26] = 72, [29] = AdvertisingTableWire.RecordLength, [30] = 68,
            [31] = 37, [32] = 33, [33] = 36, [34] = 19, [35] = 21, [49] = 263, [50] = 105,
            [55] = 571, [58] = 26, [62] = 353, [72] = 1001, [73] = 1192,
            [85] = 8001, [86] = 8001, [92] = 3061, [93] = 225, [94] = 100,
            [134] = 1153, [135] = 1109, [150] = 401, [151] = 604, [152] = 95
        };
        foreach (var number in Enumerable.Range(136, 14).Concat([154, 155]))
            records.Add(number, 203);
        if (graphical)
            foreach (var number in new[] { 131, 132, 133 })
                records.Add(number, 9224);
        return ReadOnly(records);
    }

    private static IReadOnlyDictionary<int, int> ReadOnly(Dictionary<int, int> records) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(records);
}
