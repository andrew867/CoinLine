namespace HostPlatform.Protocols.Tables;

public sealed record NumberingRule(string Id, string? NoMatch, string? Match, int Control, int Tag, int CallType,
    int[] Digits, string? Conversion = null, int? MaximumDigits = null);

public sealed record NumberingPlanChanges(NumberingRule[]? Dial, NumberingRule[]? Format);

public static class NumberingPlanWire
{
    public const int MessageType = 62;
    public const int RecordLength = 353;
    private const int DialStart = 33;
    private const int DialLength = 180;
    private const int FormatStart = DialStart + DialLength;
    private const int FormatLength = 132;
    private const int MoreDigits = 0x02;
    private const int ConversionAllowed = 0x10;

    private sealed record Node(int Offset, int Length, NumberingRule Rule);

    public static bool Supports(ReadOnlySpan<byte> record) => record.Length == RecordLength && record[0] == MessageType;

    public static NumberingPlanChanges Read(ReadOnlySpan<byte> record)
    {
        if (!Supports(record))
            throw new InvalidOperationException("The record does not match the supported numbering plan.");
        return new(
            Parse(record.Slice(DialStart, DialLength), dial: true)?.Select(node => node.Rule).ToArray(),
            Parse(record.Slice(FormatStart, FormatLength), dial: false)?.Select(node => node.Rule).ToArray());
    }

    public static void Apply(Span<byte> record, NumberingPlanChanges changes)
    {
        if (!Supports(record))
            throw new InvalidOperationException("The record does not match the supported numbering plan.");

        var dial = record.Slice(DialStart, DialLength);
        var format = record.Slice(FormatStart, FormatLength);
        var packedDial = changes.Dial == null ? null : Pack(dial, changes.Dial, dial: true);
        var packedFormat = changes.Format == null ? null : Pack(format, changes.Format, dial: false);
        if (packedDial != null)
            packedDial.CopyTo(dial);
        if (packedFormat != null)
            packedFormat.CopyTo(format);
    }

    private static List<Node>? Parse(ReadOnlySpan<byte> data, bool dial)
    {
        if (data.IndexOfAnyExcept((byte)0) < 0)
            return [];

        var nodes = new SortedDictionary<int, Node>();
        var pending = new Queue<int>();
        pending.Enqueue(0);
        var header = dial ? 6 : 4;
        while (pending.TryDequeue(out var offset))
        {
            if (nodes.ContainsKey(offset))
                continue;
            if (offset > data.Length - header - 1)
                return null;

            var control = data[offset + 2];
            var digitsStart = offset + header;
            var count = dial ? data[offset + 5] : data[digitsStart..].IndexOf((byte)0);
            var extra = dial && (control & (MoreDigits | ConversionAllowed)) == 0 ? 0 : 1;
            if (count <= 0 || digitsStart + count + extra > data.Length)
                return null;
            if (dial && (control & (MoreDigits | ConversionAllowed)) == (MoreDigits | ConversionAllowed))
                return null;

            var noMatch = data[offset];
            var match = data[offset + 1];
            var conversion = dial && (control & ConversionAllowed) != 0 ? data[digitsStart + count] : 0;
            int? maximum = dial && (control & MoreDigits) != 0 ? data[digitsStart + count] : null;
            var digits = data.Slice(digitsStart, count).ToArray().Select(value => (int)value).ToArray();
            var rule = new NumberingRule(Key(offset), Link(noMatch), Link(match), control, data[offset + 3],
                dial ? data[offset + 4] : 0, digits, (control & ConversionAllowed) != 0 && dial ? Key(conversion) : null, maximum);
            nodes[offset] = new(offset, header + count + extra, rule);
            if (noMatch != 0)
                pending.Enqueue(noMatch);
            if (match != 0)
                pending.Enqueue(match);
            if (conversion != 0)
                pending.Enqueue(conversion);
        }

        var end = 0;
        foreach (var node in nodes.Values)
        {
            if (node.Offset < end)
                return null;
            end = node.Offset + node.Length;
        }
        return nodes.Values.ToList();
    }

    private static byte[] Pack(ReadOnlySpan<byte> original, NumberingRule[] rules, bool dial)
    {
        var name = dial ? "Dialing rules" : "Display formatting";
        var previous = Parse(original, dial) ?? throw new InvalidOperationException($"{name} contain an unrecognized layout.");
        if (dial && rules.Length == 0)
            throw new InvalidOperationException("Keep at least one dialing rule.");
        if (rules.Length > original.Length / (dial ? 7 : 6))
            throw new InvalidOperationException($"{name} exceed the firmware's available space.");

        var byId = new Dictionary<string, NumberingRule>(StringComparer.Ordinal);
        var sizes = new int[rules.Length];
        var total = 0;
        for (var i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || rule.Id.Length > 64 || !byId.TryAdd(rule.Id, rule))
                throw new InvalidOperationException($"{name} require a unique ID for every row.");
            var number = i + 1;
            if (rule.Digits == null || rule.Digits.Length == 0 || rule.Digits.Length > original.Length - (dial ? 6 : 5) ||
                rule.Digits.Any(digit => digit is <= 0 or > 255))
                throw new InvalidOperationException($"{name} row {number} needs a valid nonempty digit pattern.");
            if (rule.Control is < 0 or > 255 || rule.Tag is < 0 or > 255 || rule.CallType is < 0 or > 255)
                throw new InvalidOperationException($"{name} row {number} has a field outside the byte range.");

            var extra = 1;
            if (dial)
            {
                if (rule.Conversion != null && rule.MaximumDigits.HasValue ||
                    ((rule.Control & ConversionAllowed) != 0) != (rule.Conversion != null) ||
                    ((rule.Control & MoreDigits) != 0) != rule.MaximumDigits.HasValue)
                    throw new InvalidOperationException($"Dialing rule {number} has conflicting conversion or maximum-digit settings.");
                if (rule.MaximumDigits is { } max && (max <= rule.Digits.Length || max > byte.MaxValue || rule.Match != null))
                    throw new InvalidOperationException($"Dialing rule {number} must end its match path before accepting additional digits.");
                extra = (rule.Control & (MoreDigits | ConversionAllowed)) == 0 ? 0 : 1;
            }
            else if (rule.Conversion != null || rule.MaximumDigits.HasValue)
                throw new InvalidOperationException($"Display formatting row {number} contains a dialing-only setting.");
            sizes[i] = (dial ? 6 : 4) + rule.Digits.Length + extra;
            total += sizes[i];
        }
        if (total > original.Length)
            throw new InvalidOperationException($"{name} use {total} bytes. The firmware allows {original.Length}.");

        foreach (var rule in rules)
        {
            foreach (var target in new[] { rule.NoMatch, rule.Match, rule.Conversion })
                if (target != null && !byId.ContainsKey(target))
                    throw new InvalidOperationException($"{name} contain a link to a missing row.");
            if (rule.NoMatch == rules[0].Id || rule.Match == rules[0].Id)
                throw new InvalidOperationException($"{name} cannot link back to its first row from a match or fallback.");
            if (rule.Conversion != null && ((rule.Tag & 0xF0) != 0x10 || (byId[rule.Conversion].Tag & 0xF0) != 0x10))
                throw new InvalidOperationException("A dialing conversion must link one prefix rule to another.");
        }
        if (rules.Length > 0)
            ValidatePaths(rules[0], byId, name);

        // Retain gaps and original pointers when the rows still fit their original slots.
        var keepOffsets = previous.Count == rules.Length && previous.Select((node, index) =>
            node.Rule.Id == rules[index].Id && node.Length == sizes[index]).All(matches => matches);
        var result = original.ToArray();
        var positions = new Dictionary<string, byte>(StringComparer.Ordinal);
        var occupied = new bool[original.Length];
        foreach (var node in previous)
        {
            occupied.AsSpan(node.Offset, node.Length).Fill(true);
            result.AsSpan(node.Offset, node.Length).Clear();
        }
        var offset = 0;
        for (var i = 0; i < rules.Length; i++)
        {
            var position = keepOffsets ? previous[i].Offset : offset;
            for (var index = position; index < position + sizes[i]; index++)
                if (!occupied[index] && original[index] != 0)
                    throw new InvalidOperationException($"{name} would overwrite unrecognized bytes in the record.");
            positions.Add(rules[i].Id, (byte)position);
            offset += sizes[i];
        }
        foreach (var rule in rules)
        {
            var destination = result.AsSpan(positions[rule.Id]);
            destination[0] = rule.NoMatch == null ? (byte)0 : positions[rule.NoMatch];
            destination[1] = rule.Match == null ? (byte)0 : positions[rule.Match];
            destination[2] = (byte)rule.Control;
            destination[3] = (byte)rule.Tag;
            var digitsStart = dial ? 6 : 4;
            if (dial)
            {
                destination[4] = (byte)rule.CallType;
                destination[5] = (byte)rule.Digits.Length;
            }
            for (var i = 0; i < rule.Digits.Length; i++)
                destination[digitsStart + i] = (byte)rule.Digits[i];
            if (!dial || (rule.Control & (MoreDigits | ConversionAllowed)) != 0)
                destination[digitsStart + rule.Digits.Length] = rule.Conversion == null ? (byte)(rule.MaximumDigits ?? 0) : positions[rule.Conversion];
        }
        return result;
    }

    private static void ValidatePaths(NumberingRule root, IReadOnlyDictionary<string, NumberingRule> byId, string name)
    {
        var pending = new Queue<NumberingRule>();
        var reachable = new HashSet<string>();
        var visiting = new HashSet<string>();
        var finished = new HashSet<string>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var rule))
        {
            if (!reachable.Add(rule.Id))
                continue;
            CheckMatchAndFallbackPath(rule);
            foreach (var target in new[] { rule.NoMatch, rule.Match, rule.Conversion })
                if (target != null)
                    pending.Enqueue(byId[target]);
        }
        if (reachable.Count != byId.Count)
            throw new InvalidOperationException($"{name} contain rows that cannot be reached from the first row.");

        // Conversion targets are independent prefix replacements and can reference each other.
        void CheckMatchAndFallbackPath(NumberingRule rule)
        {
            if (finished.Contains(rule.Id))
                return;
            if (!visiting.Add(rule.Id))
                throw new InvalidOperationException($"{name} contain a circular match or fallback path.");
            if (rule.NoMatch != null)
                CheckMatchAndFallbackPath(byId[rule.NoMatch]);
            if (rule.Match != null)
                CheckMatchAndFallbackPath(byId[rule.Match]);
            visiting.Remove(rule.Id);
            finished.Add(rule.Id);
        }
    }

    private static string Key(int offset) => $"row-{offset}";
    private static string? Link(int offset) => offset == 0 ? null : Key(offset);
}
