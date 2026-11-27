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
        var previousStandard = new Dictionary<int, byte[]>(standard);
        var graphical = new Dictionary<int, byte[]>(standard);
        foreach (var type in Graphics) graphical.Add(type, received["archived-demo"][type]);
        var previousGraphical = new Dictionary<int, byte[]>(graphical);
        // The graphical terminal uses the MT131 loop independently. Retain the
        // imported text messages, but do not let their MT26 rotation take priority.
        graphical[26] = NativeTableLayouts.Find(26, standard[26], NativeTableLayouts.Later)!
            .Apply(standard[26], new Dictionary<string, string> { ["onHookAds"] = "false" });
        var previousGraphicalIdle = new Dictionary<int, byte[]>(graphical);
        // The standard toll-free prefixes are ordinary direct calls, not
        // carrier-selection entries. Retain the input verbatim as a reference.
        standard[92] = DirectTollFree(standard[92]);
        graphical[92] = standard[92];
        var previousDirectStandard = new Dictionary<int, byte[]>(standard);
        var previousDirectGraphical = new Dictionary<int, byte[]>(graphical);
        standard[92] = NormalizedTollFree(standard[92]);
        graphical[92] = standard[92];

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
        starters.Add(Reference("Demo reference, later original ordinary screening", received[LaterRouting]));
        foreach (var group in AdvertisingGroups)
            starters.Add(Reference("Demo advertising, " + group["advertising/".Length..].Replace("/", " - "), received[group]));
        foreach (var group in OlderRoutingGroups.Where(group => group != OlderRouting))
            starters.Add(Reference("Demo screening, " + group["ordinary-routing/".Length..].Replace("/", " - "), received[group]));
        var digests = starters.SelectMany(set => set.Records.Values).Select(data => TablePayloadHasher.Sha256Hex(data)).ToHashSet();
        if (received.Values.SelectMany(group => group.Values).Any(data => !digests.Contains(TablePayloadHasher.Sha256Hex(data))))
            throw new InvalidOperationException("The expanded import omitted a validated demo payload.");
        return new(starters.ToArray(), [new(DemoConfigurationSeed.EarlierName, oldProfile.Id, originalOlder),
            new(DemoConfigurationSeed.LaterName, laterProfile.Id, originalLater),
            new(DemoConfigurationSeed.GraphicalName, graphProfile.Id, previousGraphical),
            new(DemoConfigurationSeed.LaterName, laterProfile.Id, previousStandard, true, " (carrier-enabled screening reference)", " (direct toll-free default)"),
            new(DemoConfigurationSeed.GraphicalName, graphProfile.Id, previousGraphicalIdle, true, " (carrier-enabled screening reference)", " (direct toll-free default)"),
            new(DemoConfigurationSeed.LaterName, laterProfile.Id, previousDirectStandard, true, " (direct flags reference)", " (normalized toll-free default)"),
            new(DemoConfigurationSeed.GraphicalName, graphProfile.Id, previousDirectGraphical, true, " (direct flags reference)", " (normalized toll-free default)")]);

        static Dictionary<int, int> Lengths(FirmwareTableProfile profile, IEnumerable<int> numbers) =>
            numbers.ToDictionary(number => number, number => profile.VerifiedLengths[number]);
        static DemoConfigurationSeed.Starter Reference(string name, IReadOnlyDictionary<int, byte[]> data) => new(name, null, data, false);
    }

    private static byte[] DirectTollFree(byte[] original)
    {
        var layout = NativeTableLayouts.Find(92, original, NativeTableLayouts.Later)!;
        var prefixes = new HashSet<string>(["1800", "1888", "1877", "1866"], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changes = new Dictionary<string, string>();
        foreach (var section in layout.Sections)
        {
            var pattern = section.Fields.Single(field => field.Key == section.Key + ".pattern").Read(original);
            if (pattern == null || !prefixes.Contains(pattern)) continue;
            string? Read(string key) => section.Fields.Single(field => field.Key == section.Key + "." + key).Read(original);
            if (Read("wildcards") != "7" || Read("classification") != "9" || Read("rate") != "255" ||
                Read("free") != "true" || Read("featureGroupB") != "false" || Read("fallback") != "false")
                throw new InvalidOperationException("A standard toll-free starter prefix has conflicting routing settings.");
            if (!seen.Add(pattern)) throw new InvalidOperationException("Duplicate standard toll-free starter prefix.");
            changes[section.Key + ".carrier"] = "false";
            changes[section.Key + ".answer"] = "true";
        }
        if (!seen.SetEquals(prefixes)) throw new InvalidOperationException("One or more standard direct toll-free prefixes are missing.");
        return layout.Apply(original, changes);
    }

    private static byte[] NormalizedTollFree(byte[] direct)
    {
        var layout = NativeTableLayouts.Find(92, direct, NativeTableLayouts.Later)!;
        var prefixes = new[] { "800", "888", "877", "866" };
        var changes = new Dictionary<string, string>();
        // These four unused positions were checked in the later demo catalogue.
        // Do not overwrite a blanked rule, reserved data or an overlapping route.
        for (var i = 0; i < prefixes.Length; i++)
        {
            var prefix = prefixes[i];
            var aliasSlot = 43 + i;
            var source = layout.Sections.Single(section => Read(section, "pattern") == "1" + prefix);
            var alias = layout.Sections[aliasSlot];
            if (direct.AsSpan(1 + aliasSlot * 17, 17).IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidOperationException("A normalized toll-free starter position is not completely unused.");
            if (Read(source, "payment") is not ("0" or "2") || Read(source, "answer") != "true" || Read(source, "carrier") != "false" ||
                source.Fields.Where(field => field.Encoding == TableFieldEncoding.Flag && !field.IsSpare &&
                    field.Key != source.Key + ".free" && field.Key != source.Key + ".answer").Any(field => field.Read(direct) != "false"))
                throw new InvalidOperationException("A standard toll-free prefix has nonordinary payment or routing settings.");
            foreach (var other in layout.Sections)
            {
                var pattern = Read(other, "pattern");
                if (pattern == null || !int.TryParse(Read(other, "wildcards"), out var wildcards) || wildcards is < 0 or > 15)
                    throw new InvalidOperationException("A starter screening pattern is unrecognized; cannot verify normalized toll-free overlap.");
                if (Overlaps(pattern, wildcards, prefix))
                    throw new InvalidOperationException("A normalized toll-free prefix overlaps an existing starter screening rule.");
            }
            changes[source.Key + ".payment"] = "2";
            changes[alias.Key + ".pattern"] = prefix;
            changes[alias.Key + ".wildcards"] = "7";
            changes[alias.Key + ".classification"] = "9";
            changes[alias.Key + ".payment"] = "2";
            changes[alias.Key + ".rate"] = "255";
            changes[alias.Key + ".free"] = "true";
            changes[alias.Key + ".answer"] = "true";
        }
        return layout.Apply(direct, changes);

        string? Read(NativeTableSection section, string key) => section.Fields.Single(field => field.Key == section.Key + "." + key).Read(direct);
        static bool Overlaps(string pattern, int trailing, string prefix)
        {
            if (pattern.Length == 0 && trailing == 0) return false;
            if (pattern.Length > 10 || pattern.Length + (trailing == 15 ? 0 : trailing) > 10 ||
                trailing != 15 && pattern.Length + trailing != 10) return false;
            return pattern.Zip(prefix).All(pair => pair.First == '?' || pair.First == pair.Second);
        }
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
        var expected = new HashSet<string> { "1800", "1888", "1877", "1866", "800", "888", "877", "866" };
        for (var i = 0; i < layout.Sections.Count; i++)
        {
            var offset = 1 + 17 * i;
            var pattern = layout.Sections[i].Fields.Single(field => field.Key.EndsWith(".pattern", StringComparison.Ordinal)).Read(later);
            if (pattern != null && expected.Contains(pattern) && (later[offset] & 7) == 3 && later[offset + 1] == 0x29 &&
                later[offset + 2] == byte.MaxValue && later[offset + 3] == 7)
                expected.Remove(pattern);
        }
        if (expected.Count != 0) throw new InvalidOperationException("The later ordinary screening defaults lack one or more unrated standard toll-free prefixes.");
    }
}
