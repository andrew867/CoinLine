namespace HostPlatform.Protocols.Tables;

public sealed record FirmwareTableProfile(
    string Id, string Label, string NativeLayout, IReadOnlyDictionary<int, int> VerifiedLengths,
    IReadOnlySet<int> TerminalAcknowledgedRecords, bool CompleteCatalogue,
    IReadOnlySet<int> SharedLayouts)
{
    public string LayoutFor(int number) => SharedLayouts.Contains(number) ? NativeTableLayouts.MultiPay : NativeLayout;
}

public static class FirmwareTableProfiles
{
    public const string MultiPay200 = "mtr19-multipay-200";
    public const string LaterMultiPay180 = "mtr2x-multipay-180-double-compressed";

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
            NativeTableLayouts.Later, LaterCatalogue(), new HashSet<int>(), true,
            new HashSet<int> { 30, 32, 35, 50, 62 })
    ];

    public static FirmwareTableProfile? Find(string? id) => Catalog.SingleOrDefault(profile => profile.Id == id);

    private static IReadOnlyDictionary<int, int> LaterCatalogue()
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
        return ReadOnly(records);
    }

    private static IReadOnlyDictionary<int, int> ReadOnly(Dictionary<int, int> records) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(records);
}
