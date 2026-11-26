using System.Text.Json;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Persistence;

/// <summary>Import the expanded manifest without merging conflicting variants or inactive references.</summary>
internal static class ExpandedDemoConfigurationSeed
{
    private const string OlderAds = "archived-mtr19-demo/mt029.bin";
    private const string LaterAds = "archived-demo/mt029.bin";
    private const string OlderRouting = "ordinary-routing/mtr19/standard/200-slot";
    private const string LaterRouting = "ordinary-routing/mtr212/180-slot";
    private const string OlderOverlay = "ordinary-routing/mtr19/mt062-overlay.json";
    private const string LaterOverlay = "ordinary-routing/mtr212/mt062-overlay.json";
    private static readonly int[] Graphics = [131, 132, 133];
    private static readonly string[] AdvertisingGroups =
    [
        "advertising/mtr19/standard-payphone", "advertising/mtr19/standard-desktop", "advertising/mtr19/standard-regional",
        "advertising/mtr19/alternate-payphone", "advertising/mtr19/alternate-desktop", "advertising/mtr19/alternate-regional",
        "advertising/mtr212/payphone", "advertising/mtr212/desktop"
    ];
    private static readonly string[] OlderRoutingGroups =
    [
        "ordinary-routing/mtr19/standard/60-slot", OlderRouting,
        "ordinary-routing/mtr19/alternate/60-slot", "ordinary-routing/mtr19/alternate/200-slot"
    ];

    internal static DemoConfigurationSeed.Bundle Read(string root, DemoConfigurationSeed.Manifest manifest, int[] earlier, int[] later)
    {
        CheckAliases(manifest);
        CheckOverlay(root, OlderOverlay);
        CheckOverlay(root, LaterOverlay);
        var oldProfile = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200)!;
        var laterProfile = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180)!;
        var graphProfile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;

        var expected = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal)
        {
            ["verified-demo"] = Lengths(oldProfile, earlier.Except([50, 73])),
            ["archived-mtr19-demo"] = Lengths(oldProfile, [29, 50, 73]),
            ["archived-demo"] = Lengths(graphProfile, later.Concat([29, 55, 85, 86, .. Graphics])),
            ["archived-demo-compact"] = new() { [101] = 403, [102] = 403, [103] = 403 },
            ["archived-demo-sequential"] = Lengths(laterProfile, [73]),
            [LaterRouting] = Lengths(laterProfile, [92]),
            ["ordinary-routing/mtr212"] = Lengths(graphProfile, [55, .. Graphics])
        };
        // Additional language records are retained without asserting that they
        // belong to the complete known later profile catalogue.
        expected["archived-demo"].Add(98, 8001);
        expected["archived-demo"].Add(99, 8001);
        foreach (var group in AdvertisingGroups) expected.Add(group, new() { [29] = AdvertisingTableWire.RecordLength });
        foreach (var group in OlderRoutingGroups) expected.Add(group, new() { [92] = group.EndsWith("/60-slot", StringComparison.Ordinal) ? 1021 : 3401 });

        var received = expected.Keys.ToDictionary(group => group, _ => new Dictionary<int, byte[]>(), StringComparer.Ordinal);
        foreach (var entry in manifest.Entries!)
        {
            if (entry.File == null || !entry.File.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Group == null || !expected.TryGetValue(entry.Group, out var group) ||
                !group.TryGetValue(entry.Number, out var length) || received[entry.Group].ContainsKey(entry.Number))
                throw new InvalidOperationException("An expanded demo record has an unexpected or duplicate type or profile group.");
            received[entry.Group].Add(entry.Number, DemoConfigurationSeed.ReadRecord(root, entry, length));
        }
        if (received.Values.Sum(group => group.Count) != 70 || expected.Any(group => !received[group.Key].Keys.ToHashSet().SetEquals(group.Value.Keys)))
            throw new InvalidOperationException("The expanded demo starter bundle is incomplete.");

        var originalOlder = new Dictionary<int, byte[]>(received["verified-demo"]);
        originalOlder.Add(50, received["archived-mtr19-demo"][50]);
        originalOlder.Add(73, received["archived-mtr19-demo"][73]);
        var originalLater = received["archived-demo"].Where(row => later.Contains(row.Key)).ToDictionary(row => row.Key, row => row.Value);
        var older = new Dictionary<int, byte[]>(originalOlder) { [29] = received["archived-mtr19-demo"][29], [92] = received[OlderRouting][92] };
        var standard = received["archived-demo"].Where(row => row.Key is not (98 or 99 or 131 or 132 or 133)).ToDictionary(row => row.Key, row => row.Value);
        standard.Add(92, received[LaterRouting][92]);
        var graphical = new Dictionary<int, byte[]>(standard);
        foreach (var type in Graphics) graphical.Add(type, received["archived-demo"][type]);

        if (!VisibleIdle(older) || !VisibleIdle(standard))
            throw new InvalidOperationException("A selected default text advertisement has no visible on-hook message.");
        CheckGraphical(graphical);
        CheckScreening(older[92], standard[92]);

        var starters = new List<DemoConfigurationSeed.Starter>
        {
            new(DemoConfigurationSeed.EarlierName, oldProfile.Id, older),
            new(DemoConfigurationSeed.LaterName, laterProfile.Id, standard),
            new(DemoConfigurationSeed.GraphicalName, graphProfile.Id, graphical),
            Reference("Demo reference, pristine blank MT29", new Dictionary<int, byte[]> { [29] = originalOlder[29] }),
            Reference("Demo reference, additional languages MT98 and MT99", received["archived-demo"].Where(row => row.Key is 98 or 99).ToDictionary(row => row.Key, row => row.Value)),
            Reference("Demo reference, MTR 2.12 singly compressed LCD", received["archived-demo-compact"]),
            Reference("Demo reference, later sequential rate comparison", received["archived-demo-sequential"]),
            Reference("Demo reference, later original graphical and key navigation", received["ordinary-routing/mtr212"])
        };
        foreach (var group in AdvertisingGroups)
            starters.Add(Reference("Demo advertising, " + group["advertising/".Length..].Replace("/", " - "), received[group]));
        foreach (var group in OlderRoutingGroups.Where(group => group != OlderRouting))
            starters.Add(Reference("Demo screening, " + group["ordinary-routing/".Length..].Replace("/", " - "), received[group]));
        var digests = starters.SelectMany(set => set.Records.Values).Select(data => TablePayloadHasher.Sha256Hex(data)).ToHashSet();
        if (received.Values.SelectMany(group => group.Values).Any(data => !digests.Contains(TablePayloadHasher.Sha256Hex(data))))
            throw new InvalidOperationException("The expanded import omitted a validated demo payload.");
        return new(starters.ToArray(), [new(DemoConfigurationSeed.EarlierName, oldProfile.Id, originalOlder),
            new(DemoConfigurationSeed.LaterName, laterProfile.Id, originalLater)]);

        static Dictionary<int, int> Lengths(FirmwareTableProfile profile, IEnumerable<int> numbers) =>
            numbers.ToDictionary(number => number, number => profile.VerifiedLengths[number]);
        static DemoConfigurationSeed.Starter Reference(string name, IReadOnlyDictionary<int, byte[]> data) => new(name, null, data, false);
    }

    private static bool VisibleIdle(IReadOnlyDictionary<int, byte[]> records)
    {
        var features = records[26];
        return (features[22] & 1) != 0 && AdvertisingTableWire.Read(records[29]).Take((features[22] & 6) != 0 ? 10 : 20)
            .TakeWhile(slot => slot.EndSequence != true).Any(slot => !string.IsNullOrWhiteSpace(slot.Text) &&
                (slot.Mode == 0 ? slot.DurationTicks > 0 : slot.Mode is >= 1 and <= 5));
    }

    private static void CheckAliases(DemoConfigurationSeed.Manifest manifest)
    {
        if (manifest.DefaultAdvertising.ValueKind != JsonValueKind.Object || manifest.OrdinaryRouting.ValueKind != JsonValueKind.Object ||
            !StringValue(manifest.DefaultAdvertising, "MTR 1.9", OlderAds) || !StringValue(manifest.DefaultAdvertising, "MTR 2.12", LaterAds) ||
            !ArrayValue(manifest.DefaultAdvertising, "graphical", Graphics.Select(n => $"archived-demo/mt{n:D3}.bin")) ||
            !StringValue(manifest.OrdinaryRouting, "earlier", OlderRouting + "/mt092.bin") ||
            !StringValue(manifest.OrdinaryRouting, "later", LaterRouting + "/mt092.bin") ||
            !ArrayValue(manifest.OrdinaryRouting, "numbering_plan_overlays", [OlderOverlay, LaterOverlay]))
            throw new InvalidOperationException("The expanded demo manifest does not select the expected populated advertising and ordinary routing defaults.");

        static bool StringValue(JsonElement obj, string key, string expected) => obj.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String && value.GetString() == expected;
        static bool ArrayValue(JsonElement obj, string key, IEnumerable<string> expected) => obj.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) &&
            value.EnumerateArray().Select(item => item.GetString()).SequenceEqual(expected);
    }

    private static void CheckOverlay(string root, string relative)
    {
        var file = DemoConfigurationSeed.CheckedFile(root, relative);
        if (file.Length is < 1 or > 8192) throw new InvalidOperationException("A demo numbering overlay is oversized or empty.");
        using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
        var obj = document.RootElement;
        if (!obj.TryGetProperty("message_type", out var type) || !type.TryGetInt32(out var message) || message != 62 ||
            !obj.TryGetProperty("record_length", out var length) || !length.TryGetInt32(out var size) || size != 353 ||
            !obj.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() != 2)
            throw new InvalidOperationException("A demo numbering overlay is invalid.");
        var spans = new Dictionary<int, int>();
        foreach (var segment in segments.EnumerateArray())
        {
            if (!segment.TryGetProperty("offset", out var offset) || !offset.TryGetInt32(out var start) ||
                !segment.TryGetProperty("hex", out var payload) || payload.ValueKind != JsonValueKind.String ||
                payload.GetString() is not { } text || text.Length % 2 != 0 || !text.All(Uri.IsHexDigit) || !spans.TryAdd(start, text.Length / 2))
                throw new InvalidOperationException("A demo numbering overlay segment is invalid.");
        }
        if (spans.Count != 2 || spans.GetValueOrDefault(1) != 18 || spans.GetValueOrDefault(32) != 321)
            throw new InvalidOperationException("The numbering overlay must preserve the existing site-specific bytes 19 through 31.");
        // No site record is fabricated or written here. The overlay remains
        // local reference data for applying to an independently configured site.
    }

    private static void CheckGraphical(IReadOnlyDictionary<int, byte[]> records)
    {
        var bitmap = 0; var mixed = 0;
        foreach (var type in Graphics)
        {
            var data = records[type];
            var layout = NativeTableLayouts.Find(type, data, NativeTableLayouts.Later)
                ?? throw new InvalidOperationException("A graphical demo record does not match its native layout.");
            var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
            if (type == 131 && fields.Single(field => field.Key == "onHook").Read(data) != "true")
                throw new InvalidOperationException("The graphical demo rotation is not enabled.");
            for (var slot = 0; slot < 8; slot++)
            {
                var kind = fields.Single(field => field.Key == $"screen-{slot}.kind").Read(data);
                if (kind == "66") bitmap++;
                else if (kind == "77") mixed++;
            }
        }
        if (bitmap != 20 || mixed != 4) throw new InvalidOperationException("The expanded graphical demo must include twenty bitmap and four mixed screens.");
    }

    private static void CheckScreening(byte[] older, byte[] later)
    {
        for (var offset = 1; offset < older.Length; offset += 17)
            if ((older[offset + 4] >> 4) != 0 && (older[offset] & 1) != 0 && older[offset + 2] != byte.MaxValue)
                throw new InvalidOperationException("The older demo has a free screening entry without the unrated rate index.");
        var layout = NativeTableLayouts.Find(92, later, NativeTableLayouts.Later)
            ?? throw new InvalidOperationException("The later ordinary screening layout is unknown.");
        var expected = new HashSet<string> { "1800", "1888", "1877", "1866" };
        for (var i = 0; i < layout.Sections.Count; i++)
        {
            var offset = 1 + 17 * i;
            var pattern = layout.Sections[i].Fields.Single(field => field.Key.EndsWith(".pattern", StringComparison.Ordinal)).Read(later);
            if (pattern != null && expected.Contains(pattern) && (later[offset] & 1) != 0 && (later[offset + 1] & 15) == 9 && later[offset + 2] == byte.MaxValue)
                expected.Remove(pattern);
        }
        if (expected.Count != 0) throw new InvalidOperationException("The later ordinary screening defaults lack one or more unrated standard toll-free prefixes.");
    }
}
