namespace HostPlatform.Protocols.Tables;

public static class QuickAccessKeyWire
{
    public const byte MessageType = 55;
    public const int SlotCount = 10;
    public const int SlotLength = 57;
    public const int RecordLength = 1 + SlotCount * SlotLength;
    internal const byte FreeCall = 0x01;
    internal const byte AnswerSupervision = 0x02;
    internal const byte CarrierPrefix = 0x04;
    internal const byte EnableKeypad = 0x20;
    internal const byte Information = 0x40;
    internal const byte FeatureGroupB = 0x80;
    internal const byte Advertising = 0x01;

    public static void Clear(Span<byte> record, NativeTableLayout layout, IReadOnlyList<int> keys,
        IReadOnlyDictionary<string, string> changes)
    {
        if (layout.Number != MessageType || record.Length != RecordLength || record[0] != MessageType)
            throw new InvalidOperationException("Quick-access keys can only be cleared in a compatible MT55 record.");
        var distinct = new HashSet<int>();
        foreach (var key in keys)
        {
            if (key is < 1 or > SlotCount || !distinct.Add(key))
                throw new InvalidOperationException("Select distinct quick-access keys from 1 through 10.");
            if (changes.Keys.Any(field => field.StartsWith($"key-{key - 1}.", StringComparison.Ordinal)))
                throw new InvalidOperationException("Clear or edit each quick-access key in the same save.");
        }

        foreach (var key in keys)
        {
            var slot = record.Slice(1 + (key - 1) * SlotLength, SlotLength);
            slot[0] &= unchecked((byte)~(FreeCall | AnswerSupervision | CarrierPrefix | EnableKeypad | Information | FeatureGroupB));
            // Blank classification, reference, destination, two display lines, and customer reference.
            slot[1..55].Clear();
            slot[55] &= layout.Profile == NativeTableLayouts.MultiPay ? (byte)0xF7 : (byte)0xF0;
            slot[56] &= unchecked((byte)~Advertising);
        }
    }
}
