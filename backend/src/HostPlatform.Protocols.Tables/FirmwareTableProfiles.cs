namespace HostPlatform.Protocols.Tables;

public sealed record FirmwareTableProfile(
    string Id, string Label, string NativeLayout, IReadOnlyDictionary<int, int> VerifiedLengths);

public static class FirmwareTableProfiles
{
    // Record sizes acknowledged by the 200-entry, compressed-dialing Multi-pay build.
    public static IReadOnlyList<FirmwareTableProfile> Catalog { get; } =
    [
        new("mtr19-multipay-200", "MTR 1.9 Multi-pay, 200-entry screening", NativeTableLayouts.MultiPay,
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(new Dictionary<int, int>
            {
                [21] = 18, [22] = 661, [26] = 72, [29] = AdvertisingTableWire.RecordLength, [31] = 37,
                [50] = 105, [62] = 353, [73] = 1192, [92] = 3401, [93] = 225, [101] = 403
            }))
    ];

    public static FirmwareTableProfile? Find(string? id) => Catalog.SingleOrDefault(profile => profile.Id == id);
}
