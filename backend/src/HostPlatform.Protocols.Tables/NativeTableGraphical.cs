using static HostPlatform.Protocols.Tables.TableFieldEncoding;

namespace HostPlatform.Protocols.Tables;

public static partial class NativeTableLayouts
{
    private const int GraphicalHeader = 8;
    private const int ScreensPerRecord = 8;
    private const int ScreenSize = 1152;
    private const int ScreenBody = 1044;
    private const int ScreenAttributes = 1 + ScreenBody;
    private const int ScreenButtons = 1062;
    private const int ButtonSize = 15;
    private const int ButtonsPerScreen = 6;
    private const int MixedPanelSize = 261;
    private const int MixedPanelAttributes = 133;
    private const int MixedPanelButtons = 171;
    private const byte BitmapScreen = (byte)'B';
    private const byte TextScreen = (byte)'T';
    private const byte MixedScreen = (byte)'M';
    private const byte NoScreen = byte.MaxValue;
    private const int GraphicalRecordLength = GraphicalHeader + ScreenSize * ScreensPerRecord;

    private static readonly TableFieldChoice[] ScreenKinds =
        [new(0, "Undefined"), new(BitmapScreen, "Bitmap"), new(TextScreen, "Text"), new(MixedScreen, "Mixed")];
    private static readonly (int, string)[] EntranceChoices =
    [
        (0, "Static"), (1, "Flash"), (2, "Alternate with next screen"),
        (3, "Left to right"), (4, "Left to right, flash"), (5, "Left to right, alternate"),
        (6, "Right to left"), (7, "Right to left, flash"), (8, "Right to left, alternate"),
        (9, "Top to bottom"), (10, "Top to bottom, flash"), (11, "Top to bottom, alternate"),
        (12, "Bottom to top"), (13, "Bottom to top, flash"), (14, "Bottom to top, alternate"), (255, "Disabled")
    ];
    private static readonly (int, string)[] ExitChoices =
        [(0, "Remain until next screen"), (1, "Clear display"), (2, "Left to right"), (3, "Right to left"),
         (4, "Top to bottom"), (5, "Bottom to top"), (255, "Disabled")];

    internal static bool IsGraphicalRecord(int number) => number is >= 131 and <= 133;

    private static int ScreenOffset(int index) => GraphicalHeader + index * ScreenSize;

    private static NativeTableField ScreenKindField(int index) =>
        new($"screen-{index}.kind", "Screen type", ScreenOffset(index), GraphicalKind, Choices: ScreenKinds);

    internal static NativeTableSection[] PrepareGraphicalChanges(Span<byte> edited, IReadOnlyDictionary<string, string> changes)
    {
        for (var screen = 0; screen < ScreensPerRecord; screen++)
        {
            var field = ScreenKindField(screen);
            if (!changes.TryGetValue(field.Key, out var value) || value == field.Read(edited))
                continue;
            if (value == null)
                throw new InvalidOperationException("Choose a screen type.");

            var oldKind = edited[field.Offset];
            field.Write(edited, value);
            if (edited[field.Offset] == oldKind)
                continue;
            edited.Slice(field.Offset + 1, ScreenBody).Clear();
            if (edited[field.Offset] != MixedScreen)
                continue;

            for (var panel = 0; panel < 4; panel++)
            {
                var start = field.Offset + 1 + panel * MixedPanelSize;
                InitializeScreenControls(edited, start + MixedPanelAttributes, start + MixedPanelButtons);
            }
        }
        return GraphicalSections(edited);
    }

    private static NativeTableSection[] GraphicalSections(ReadOnlySpan<byte> record)
    {
        var sections = new List<NativeTableSection>
        {
            Section("rotation", "Graphical screens and rotation",
                Field("statDuration", "Statistics collection duration (days)", 1, Word,
                    help: "Use MT131 to configure the reporting period. Zero disables collection."),
                Toggle("onHook", "Display while on hook", 3, 1), Toggle("offHook", "Display while off hook", 3, 2),
                Toggle("connected", "Display during connected calls", 3, 4),
                Toggle("idleNavigation", "Allow on-hook and off-hook navigation", 3, 8),
                Toggle("connectedNavigation", "Allow navigation during connected calls", 3, 16),
                ScreenLink("offHookScreen", "Off-hook entry screen", 4), ScreenLink("connectedScreen", "Connected-call screen", 5),
                Field("offHookDuration", "Off-hook display duration (seconds)", 6))
        };
        for (var index = 0; index < ScreensPerRecord; index++)
        {
            var offset = ScreenOffset(index);
            var name = $"Screen {(record[0] - 131) * ScreensPerRecord + index + 1}";
            var key = $"screen-{index}";
            var kind = record[offset];
            var fields = new List<NativeTableField> { ScreenKindField(index) };
            if (kind == BitmapScreen)
                fields.Add(new($"{key}.bitmap", "Screen artwork", offset + 1, Bitmap, Size: 1008, BitmapTypeOffset: offset));
            if (kind == TextScreen)
                fields.Add(new($"{key}.text", "Screen text", offset + 1, GraphicalText, Size: GraphicalScreenText.Width));
            fields.AddRange(ScreenControls(key, offset + ScreenAttributes));
            sections.Add(new(key, kind == MixedScreen ? $"{name} · Mixed screen defaults" : name, fields, GraphicalScreenIndex: index));
            if (kind == MixedScreen)
            {
                for (var panel = 0; panel < 4; panel++)
                {
                    var panelOffset = offset + 1 + panel * MixedPanelSize;
                    var panelKey = $"{key}.panel-{panel}";
                    var panelName = $"{name} · Panel {panel + 1}";
                    sections.Add(new(panelKey, panelName,
                        [.. PanelTextFields(panelKey, panelOffset), .. ScreenControls(panelKey, panelOffset + MixedPanelAttributes)],
                        GraphicalScreenIndex: index, ShowPanelPreview: true));
                    AddGraphicalButtons(sections, index, panelName, panelOffset + MixedPanelButtons, panelKey);
                }
            }
            AddGraphicalButtons(sections, index, kind == MixedScreen ? $"{name} · Defaults" : name, offset + ScreenButtons);
        }
        return sections.ToArray();
    }

    private static IEnumerable<NativeTableField> PanelTextFields(string key, int offset)
    {
        const int lineWidth = GraphicalScreenText.CellsPerRow;
        for (var row = 0; row < 4; row++)
            yield return new($"{key}.line-{row}", $"Text line {row + 1}", offset + row * lineWidth, DisplayText, lineWidth);
        for (var row = 0; row < 3; row++)
        {
            var start = offset + (4 + row) * lineWidth;
            yield return new($"{key}.label-{row * 2}", $"Key {row * 2 + 1} label", start, DisplayText, 9);
            yield return new($"{key}.label-{row * 2 + 1}", $"Key {row * 2 + 2} label", start + 10, DisplayText, 9);
        }
    }

    private static IEnumerable<NativeTableField> ScreenControls(string key, int offset)
    {
        yield return Field($"{key}.dwell", "Screen display duration (10 ms ticks)", offset, Word);
        yield return Field($"{key}.flash", "Flash rate", offset + 2, GraphicalFlashRate);
        yield return Field($"{key}.owner", "Screen owner code", offset + 11, Word,
            help: "The terminal reports usage for screens with nonzero owner codes in MT153. A mixed screen uses its main screen's owner code for the report.");
        yield return ScreenLink($"{key}.next", "Next screen", offset + 13);
        yield return ScreenLink($"{key}.connected", "Connected-call screen", offset + 14);
        for (var loop = 0; loop < 4; loop++)
        {
            yield return Select($"{key}.enter-{loop}", $"Loop {loop + 1} entrance", offset + 3 + loop, EntranceChoices);
            yield return Select($"{key}.exit-{loop}", $"Loop {loop + 1} exit", offset + 7 + loop, ExitChoices);
        }
    }

    private static void AddGraphicalButtons(List<NativeTableSection> sections, int screen, string name, int offset, string? panel = null)
    {
        for (var index = 0; index < ButtonsPerScreen; index++)
        {
            var key = panel == null ? $"button-{screen * ButtonsPerScreen + index}" : $"{panel}.button-{index}";
            var button = offset + index * ButtonSize;
            sections.Add(new(key, $"{name}, key {index + 1}",
            [
                Field($"{key}.owner", "Key owner code", button, Word,
                    help: "MT153 reports the key owner code from the main screen. Mixed panels contribute their presses to the same screen and key position."),
                new($"{key}.digits", "Dialed digits", button + 6, KeypadDial, 8),
                ScreenLink($"{key}.next", "Next screen", button + 14),
                Bits($"{key}.type", "Call classification", button + 3, 15, CallKinds),
                Bits($"{key}.payment", "Payment classification", button + 3, 240, PaymentKinds),
                Field($"{key}.rate", "Rate index", button + 5),
                Field($"{key}.icon", "Icon ID", button + 4, GraphicalIcon),
                Toggle($"{key}.free", "Free call", button + 2, 1), Toggle($"{key}.answer", "Require answer supervision", button + 2, 2),
                Toggle($"{key}.mute", "Mute transmitter until answer supervision", button + 2, 16),
                Toggle($"{key}.disableKeypad", "Disable keypad during the call", button + 2, 32),
                Toggle($"{key}.unrestricted", "Allow during restricted service", button + 2, 64),
                Toggle($"{key}.targetedCdr", "Collect targeted call records", button + 2, 128)
            ], GraphicalScreenIndex: screen));
        }
    }

    private static NativeTableField ScreenLink(string key, string label, int offset) => Field(key, label, offset, GraphicalLink);

    private static void InitializeGraphicalRecord(Span<byte> record)
    {
        record[4] = record[5] = NoScreen;
        for (var i = 0; i < ScreensPerRecord; i++)
        {
            var start = ScreenOffset(i);
            InitializeScreenControls(record, start + ScreenAttributes, start + ScreenButtons);
        }
    }

    private static void InitializeScreenControls(Span<byte> record, int attributes, int buttons)
    {
        record.Slice(attributes + 4, 3).Fill(NoScreen);
        record.Slice(attributes + 8, 3).Fill(NoScreen);
        record[attributes + 13] = record[attributes + 14] = NoScreen;
        for (var i = 0; i < ButtonsPerScreen; i++)
            record[buttons + i * ButtonSize + 14] = NoScreen;
    }
}
