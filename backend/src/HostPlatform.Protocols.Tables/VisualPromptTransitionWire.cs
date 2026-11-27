namespace HostPlatform.Protocols.Tables;

public sealed record VisualPromptTransitionMessage(int SourceMessage, int? TargetMessage, string SourceLabel, string? TargetLabel,
    string?[] SourceLines, string?[] TargetLines, string Status, bool Eligible, bool Selected, int PotentialChangedBytes);
public sealed record VisualPromptTransitionFinding(bool Blocking, string Message);
public sealed record VisualPromptTransitionProjection(byte[] Payload, VisualPromptTransitionMessage[] Messages, int ChangedBytes,
    VisualPromptTransitionFinding[] Findings);

/// <summary>Project explicitly selected two-line messages between the fixed-cell prompt layouts.</summary>
public static class VisualPromptTransitionWire
{
    public const int EarlierLength = 2801;
    public const int LaterLength = 8001;
    private const int Cells = 20;
    private const int EarlierMessages = 70;

    public static int? TargetType(int sourceType) => sourceType switch { 27 => 85, 28 => 86, _ => null };

    public static VisualPromptTransitionProjection Project(byte[] source, byte[] target, int[] selectedMessages)
    {
        if (source.Length != EarlierLength || TargetType(source[0]) is not { } targetType ||
            target.Length != LaterLength || target[0] != targetType)
            throw new InvalidOperationException("Choose complete earlier and later prompt records from the same language slot.");
        if (selectedMessages.Length > EarlierMessages || selectedMessages.Any(id => id is < 0 or >= EarlierMessages) ||
            selectedMessages.Distinct().Count() != selectedMessages.Length)
            throw new InvalidOperationException("Choose each earlier message position at most once.");

        var output = target.ToArray();
        var messages = new List<VisualPromptTransitionMessage>();
        var findings = new List<VisualPromptTransitionFinding>();
        var changed = 0;
        for (var sourceMessage = 0; sourceMessage < EarlierMessages; sourceMessage++)
        {
            var label = VisualPromptCatalog.MessageName(sourceMessage, expanded: false) ?? "Unidentified message";
            var targetMessage = TargetMessage(sourceMessage);
            var sourceLines = ReadLines(source, sourceMessage);
            var selected = selectedMessages.Contains(sourceMessage);
            if (targetMessage is not { } destination)
            {
                messages.Add(new(sourceMessage, null, label, null, sourceLines, [], "unmapped", false, selected, 0));
                if (selected) findings.Add(new(true, $"Earlier message {sourceMessage} has no compatible mapped target position."));
                continue;
            }

            var targetLines = ReadLines(target, destination);
            var destinationLabel = VisualPromptCatalog.MessageName(destination, expanded: true);
            if (sourceLines.Any(line => line == null) || targetLines.Any(line => line == null))
            {
                messages.Add(new(sourceMessage, destination, label, destinationLabel, sourceLines, targetLines,
                    "unknown-glyph", false, selected, 0));
                if (selected) findings.Add(new(true, $"Message {sourceMessage} or target message {destination} contains an unmapped display cell. Review it in the prompt worksheet instead."));
                continue;
            }

            var potential = 0;
            for (var line = 0; line < 2; line++)
            {
                // Equal visible text retains the candidate's exact cell padding.
                if (sourceLines[line] == targetLines[line]) continue;
                var previous = Line(source, sourceMessage, line);
                var candidate = Line(target, destination, line);
                for (var index = 0; index < Cells; index++) if (previous[index] != candidate[index]) potential++;
                if (selected) previous.CopyTo(output.AsSpan(1 + (destination * 2 + line) * Cells, Cells));
            }
            if (selected) changed += potential;
            messages.Add(new(sourceMessage, destination, label, destinationLabel, sourceLines, targetLines,
                potential == 0 ? "same" : "different", true, selected, potential));
        }
        return new(output, messages.ToArray(), changed, findings.ToArray());
    }

    private static int? TargetMessage(int source)
    {
        // These positions have changed or unassigned meanings. The earlier
        // emergency message has its own later position, not later message 28.
        if (source is 0 or 1 or >= 51 and <= 55) return null;
        var destination = source == 28 ? 70 : source;
        var previous = VisualPromptCatalog.MessageName(source, expanded: false);
        var next = VisualPromptCatalog.MessageName(destination, expanded: true);
        return previous != null && previous == next ? destination : null;
    }

    private static string?[] ReadLines(byte[] record, int message) =>
        [VisualPromptText.Read(Line(record, message, 0)), VisualPromptText.Read(Line(record, message, 1))];

    private static ReadOnlySpan<byte> Line(byte[] record, int message, int line) =>
        record.AsSpan(1 + (message * 2 + line) * Cells, Cells);
}
