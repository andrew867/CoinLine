using System.Text.Json;

namespace HostPlatform.Api.Services;

public static class InmateFeatureSchema
{
    public sealed record Choice(int Value, string Label);
    public sealed record Field(string Key, string Label, string Kind, JsonElement DefaultValue, int? Minimum = null,
        int? Maximum = null, IReadOnlyList<Choice>? Choices = null);
    public sealed record Section(string Key, string Label, IReadOnlyList<Field> Fields);

    private static readonly Choice[] PromptChoices = [new(0, "None"), new(1, "Institution announcement"),
        new(2, "Monitoring announcement"), new(3, "Both announcements")];
    private static readonly Choice[] RecordingChoices = [new(1, "Do not record"), new(2, "Record"), new(3, "Record and monitor")];

    public static IReadOnlyList<Section> Sections { get; } =
    [
        new("pin", "PIN entry", [
            Select("pinStrategy", "PIN entry method", 0, [new(0, "None"), new(1, "Keypad"), new(2, "PIN card")]),
            Number("pinLength", "PIN length (digits)", 7, 6, 20)
        ]),
        new("processing", "Call processing", [
            Flag("validateCollect", "Validate collect calls through the manager"),
            Flag("requireCollectPin", "Require PIN for collect calls"),
            Flag("waitForCollectTone", "Wait for the collect call acceptance tone"),
            Flag("spillCollectCode", "Send the operator code at the collect tone"),
            Flag("spillCollectPin", "Send the PIN at the collect tone"),
            Flag("validateAccepted", "Validate other accepted calls through the manager"),
            Flag("requireAcceptedPin", "Require PIN for other accepted calls"),
            Flag("requireFreePin", "Require PIN for free calls")
        ]),
        new("prompts", "Voice announcements", [
            Select("dataPrompt", "During a data call", 2, PromptChoices),
            Select("answeredPrompt", "When the call is answered", 3, PromptChoices),
            Select("repeatPrompt", "During an established call", 0, PromptChoices),
            Number("repeatInterval", "Repeat interval (seconds)", 60, 30, 1800)
        ]),
        new("warnings", "Disconnect warnings", [
            Number("firstWarning", "Voice warning before disconnect (seconds)", 60, 10, 300),
            Number("secondWarning", "Tone before disconnect (seconds)", 10, 1, 30)
        ]),
        new("recording", "Recording equipment", [
            Flag("recorderPresent", "Recording equipment is connected"),
            Dtmf("noRecordCode", "Do not record code", 2),
            Dtmf("recordCode", "Record code", 2),
            Dtmf("recordAndMonitorCode", "Record and monitor code", 2),
            Dtmf("reservedRecorderCode1", "Reserved code 1", 2),
            Dtmf("reservedRecorderCode2", "Reserved code 2", 2),
            Dtmf("reservedRecorderCode3", "Reserved code 3", 2),
            Select("collectRecording", "Collect call default", 1, RecordingChoices),
            Select("acceptedRecording", "Other accepted call default", 1, RecordingChoices),
            Select("freeRecording", "Free call default", 1, RecordingChoices)
        ]),
        new("timers", "Call timers", [
            Number("nonCollectEstablished", "Assumed answer time for other calls (seconds)", 5, 0, 300),
            Number("collectEstablished", "Assumed answer time for collect calls (seconds)", 10, 0, 300),
            Number("collectKeypad", "Collect keypad window (seconds)", 60, 0, 300),
            Number("collectToneDelay", "Delay when no collect tone is expected (seconds)", 2, 0, 300),
            Number("collectToneTimeout", "Collect tone timeout (seconds)", 2, 0, 300),
            Number("reservedTimer", "Reserved timer (seconds)", 0, 0, 300)
        ]),
        new("other", "Other settings", [
            Flag("autoDialPin", "Require PIN for auto-dial"),
            Flag("managerValidationRequired", "Require a reply from the manager for validation"),
            Flag("answerSupervision", "Use answer supervision when available"),
            Select("rejectionPrompt", "Call rejection announcement", 0, [new(0, "Generic"), new(1, "Give the reason")]),
            Dtmf("operatorCode", "Automated operator code", 3)
        ])
    ];

    private static readonly Dictionary<string, Field> Fields = Sections.SelectMany(section => section.Fields)
        .ToDictionary(field => field.Key, StringComparer.Ordinal);

    public static (Dictionary<string, JsonElement> Values, Dictionary<string, string[]> Errors) Validate(
        IReadOnlyDictionary<string, JsonElement>? settings)
    {
        var values = Fields.ToDictionary(entry => entry.Key, entry => entry.Value.DefaultValue, StringComparer.Ordinal);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (settings != null)
        {
            foreach (var (key, value) in settings)
            {
                if (!Fields.TryGetValue(key, out var field))
                {
                    errors[$"settings.{key}"] = ["Unknown configuration field."];
                    continue;
                }

                if (ValidateField(field, value) is { } error)
                {
                    errors[$"settings.{key}"] = [$"{field.Label}. {error}"];
                    continue;
                }

                values[key] = field.Kind == "dtmf"
                    ? JsonSerializer.SerializeToElement(value.GetString()!.ToUpperInvariant())
                    : value;
            }
        }

        if (!errors.ContainsKey("settings.firstWarning") && !errors.ContainsKey("settings.secondWarning") &&
            values["firstWarning"].GetInt32() - values["secondWarning"].GetInt32() < 10)
            errors["settings.firstWarning"] = ["The voice warning must be at least 10 seconds before the tone."];
        return (values, errors);
    }

    private static string? ValidateField(Field field, JsonElement value) => field.Kind switch
    {
        "checkbox" when value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) => "Use true or false.",
        "number" when value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) ||
            number < field.Minimum || number > field.Maximum => $"Enter an integer from {field.Minimum} to {field.Maximum}.",
        "select" when value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var choice) ||
            !field.Choices!.Any(option => option.Value == choice) => "Choose a listed option.",
        "dtmf" when value.ValueKind != JsonValueKind.String || value.GetString()!.Length > field.Maximum ||
            !value.GetString()!.All(IsDtmf) => $"Enter up to {field.Maximum} characters using 0 to 9, A to D, * or #.",
        _ => null
    };

    private static bool IsDtmf(char value) => value is (>= '0' and <= '9') or (>= 'A' and <= 'D') or (>= 'a' and <= 'd') or '*' or '#';

    private static Field Flag(string key, string label) => new(key, label, "checkbox", JsonSerializer.SerializeToElement(false));

    private static Field Number(string key, string label, int value, int min, int max) =>
        new(key, label, "number", JsonSerializer.SerializeToElement(value), min, max);

    private static Field Select(string key, string label, int value, IReadOnlyList<Choice> options) =>
        new(key, label, "select", JsonSerializer.SerializeToElement(value), Choices: options);

    private static Field Dtmf(string key, string label, int max) =>
        new(key, label, "dtmf", JsonSerializer.SerializeToElement(string.Empty), Maximum: max);
}
