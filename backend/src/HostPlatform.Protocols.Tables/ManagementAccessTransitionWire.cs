namespace HostPlatform.Protocols.Tables;

/// <summary>Carry documented management-access fields between identified MT21 layouts.</summary>
public static class ManagementAccessTransitionWire
{
    public static byte[] CarryForward(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, string registeredWireId)
    {
        var sourceLayout = Layout(source);
        var targetLayout = Layout(target);
        if (registeredWireId.Length != 10 || !registeredWireId.All(char.IsAsciiDigit) ||
            Field(sourceLayout, "terminalNumber").Read(source) != registeredWireId)
            throw new InvalidOperationException("The assigned MT21 must contain the phone's registered decimal wire identity.");

        var result = target.ToArray();
        Field(targetLayout, "terminalNumber").Write(result, registeredWireId);
        foreach (var key in new[] { "primary", "alternate" })
        {
            var from = Field(sourceLayout, key);
            var to = Field(targetLayout, key);
            var value = from.Read(source);
            if (value == null || (key == "primary" && value.Length == 0))
                throw new InvalidOperationException($"The assigned MT21 has an invalid or missing {from.Label.ToLowerInvariant()}.");

            // A decoded terminator can hide unexpected bytes to its right. Do
            // not discard those bytes as part of an automatic conversion.
            var canonical = new byte[sourceLayout.Length]; canonical[0] = 21;
            from.Write(canonical, value);
            if (!source.Slice(from.Offset, from.Width).SequenceEqual(canonical.AsSpan(from.Offset, from.Width)))
                throw new InvalidOperationException($"The assigned MT21 has noncanonical data in its {from.Label.ToLowerInvariant()}; review it before conversion.");
            if (value.Length > to.TextLimit!.Value)
                throw new InvalidOperationException($"The {from.Label.ToLowerInvariant()} does not fit the target management-access layout.");

            // Write a canonical field independently of the target's old value.
            // The target layout's ADSI identifiers and all remaining bytes stay intact.
            var encoded = new byte[targetLayout.Length]; encoded[0] = 21;
            to.Write(encoded, value);
            encoded.AsSpan(to.Offset, to.Width).CopyTo(result.AsSpan(to.Offset, to.Width));
        }
        return result;
    }

    private static NativeTableLayout Layout(ReadOnlySpan<byte> record) =>
        record.Length is 18 or 48 && record[0] == 21 && NativeTableLayouts.Candidates(21, record).SingleOrDefault() is { } layout
            ? layout : throw new InvalidOperationException("Management-access conversion requires the identified 18-byte or 48-byte MT21 layout.");

    private static NativeTableField Field(NativeTableLayout layout, string key) =>
        layout.Sections.SelectMany(section => section.Fields).Single(field => field.Key == key);
}
