using System.Globalization;

namespace HostPlatform.Protocols.Tables;

/// <summary>Creates a complete editable graphical starter from the bundled operator artwork.</summary>
public static class GraphicalStarterWire
{
    public const int FrameCount = 16;
    public const int FrameTicks = 50;
    public const int AdvertisementCount = 4;
    public const int AdvertisementTicks = 500;
    public const int RotationScreenCount = FrameCount + AdvertisementCount;
    private const int ScreensPerRecord = 8;
    private const int MenuScreen = RotationScreenCount;
    private const int PixelBytes = 1008;
    private static readonly string ResourcePrefix = typeof(GraphicalStarterWire).Namespace + ".Assets.GraphicalStarter.";

    public static IReadOnlyDictionary<int, byte[]> Create(bool enableSampleMenu = false)
    {
        var records = new Dictionary<int, byte[]>();
        foreach (var number in new[] { 131, 132, 133 })
        {
            var template = NativeTableLayouts.DraftTemplates(number).Single();
            var bytes = NativeTableLayouts.CreateDraftTemplate(template);
            var layout = NativeTableLayouts.Find(number, bytes, template.Profile)!;
            var edits = new Dictionary<string, string>();
            if (number == 131)
            {
                edits["onHook"] = "true";
                edits["idleNavigation"] = "true";
                if (enableSampleMenu)
                {
                    edits["offHook"] = "true";
                    edits["offHookScreen"] = MenuScreen.ToString(CultureInfo.InvariantCulture);
                }
            }
            for (var slot = 0; slot < ScreensPerRecord; slot++)
            {
                var global = (number - 131) * ScreensPerRecord + slot;
                if (global >= RotationScreenCount)
                    break;
                var prefix = $"screen-{slot}";
                edits[$"{prefix}.kind"] = "66";
                edits[$"{prefix}.bitmap"] = Convert.ToBase64String(ReadFrame(global + 1));
                edits[$"{prefix}.dwell"] = (global < FrameCount ? FrameTicks : AdvertisementTicks).ToString(CultureInfo.InvariantCulture);
                edits[$"{prefix}.next"] = ((global + 1) % RotationScreenCount).ToString(CultureInfo.InvariantCulture);
            }
            if (number == 133)
            {
                // An original, unrouted example menu. It remains detached from
                // the on-hook loop; optional off-hook entry is explicit.
                edits[$"screen-{MenuScreen % ScreensPerRecord}.kind"] = "77";
                AddPanel(0, ["CoinLine services", "Explore the menu", "Use the side keys", ""], ["Info", "Help", "", "", "", ""],
                    new Dictionary<int, int> { [0] = MenuScreen | 64, [1] = MenuScreen | 128 });
                AddPanel(1, ["Information", "Sample display only", "No call is placed", ""], ["Back", "", "", "", "", ""],
                    new Dictionary<int, int> { [0] = MenuScreen });
                AddPanel(2, ["Help", "Use the keypad", "Sample navigation", ""], ["Back", "", "", "", "", ""],
                    new Dictionary<int, int> { [0] = MenuScreen });
            }
            records.Add(number, layout.Apply(bytes, edits));

            void AddPanel(int index, string[] lines, string[] keys, IReadOnlyDictionary<int, int> links)
            {
                var prefix = $"screen-{MenuScreen % ScreensPerRecord}.panel-{index}";
                for (var row = 0; row < lines.Length; row++)
                    edits[$"{prefix}.line-{row}"] = lines[row];
                for (var key = 0; key < keys.Length; key++)
                    edits[$"{prefix}.label-{key}"] = keys[key];
                edits[$"{prefix}.dwell"] = "1000";
                foreach (var (key, address) in links)
                    edits[$"{prefix}.button-{key}.next"] = address.ToString(CultureInfo.InvariantCulture);
            }
        }
        return records;
    }

    private static byte[] ReadFrame(int frame)
    {
        using var stream = typeof(GraphicalStarterWire).Assembly.GetManifestResourceStream(
            ResourcePrefix + $"frame-{frame:00}.bin") ?? throw new InvalidDataException("Missing graphical starter artwork.");
        var data = new byte[PixelBytes];
        stream.ReadExactly(data);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("Invalid graphical starter artwork length.");
        return data;
    }
}
