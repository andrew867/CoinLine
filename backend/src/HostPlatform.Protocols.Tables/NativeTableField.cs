using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace HostPlatform.Protocols.Tables;

public enum TableFieldEncoding
{
    UInt8, Word, SignedWord, SignedLong, Flag, Bits, Text, Bcd, Dial, Bitmap, MessagingKey, DisplayText,
    KeypadDial, GraphicalKind, GraphicalText, GraphicalLink, GraphicalFlashRate, GraphicalIcon, CredentialBcd, HexBytes,
    TerminatedDisplayText, CardStandard, CardServiceCode, CredentialHex, BigEndianWord, BigEndianBits, SignedByte,
    SmartDiscountMethod, UnsignedLong, PaddedCredentialBcd, TerminalCalendar, TerminalTime
}

public sealed record TableFieldChoice(int Value, string Label);

public sealed record NativeTableField(
    string Key, string Label, int Offset, TableFieldEncoding Encoding = TableFieldEncoding.UInt8,
    int Size = 1, int Mask = 0, int? Minimum = null, int? Maximum = null,
    IReadOnlyList<TableFieldChoice>? Choices = null, string? Help = null, int? BitmapTypeOffset = null)
{
    public string Editor => Encoding switch
    {
        TableFieldEncoding.Flag => "checkbox",
        TableFieldEncoding.Bitmap => "bitmap",
        TableFieldEncoding.MessagingKey or TableFieldEncoding.CredentialBcd => "password",
        TableFieldEncoding.CredentialHex => "password-hex",
        TableFieldEncoding.PaddedCredentialBcd => "password-pin",
        TableFieldEncoding.TerminalCalendar => "datetime",
        TableFieldEncoding.TerminalTime => "time",
        TableFieldEncoding.GraphicalKind => "screen-kind",
        TableFieldEncoding.GraphicalText => "screen-text",
        TableFieldEncoding.GraphicalLink => "screen-link",
        TableFieldEncoding.GraphicalFlashRate => "flash-rate",
        TableFieldEncoding.GraphicalIcon => "screen-icon",
        TableFieldEncoding.CardStandard => "card-standard",
        TableFieldEncoding.SmartDiscountMethod => "discount-method",
        TableFieldEncoding.Text or TableFieldEncoding.DisplayText or TableFieldEncoding.TerminatedDisplayText or TableFieldEncoding.Bcd or TableFieldEncoding.Dial
            or TableFieldEncoding.KeypadDial or TableFieldEncoding.HexBytes or TableFieldEncoding.CardServiceCode => "text",
        _ when Choices != null => "select",
        _ => "number"
    };

    public int Width => Encoding switch
    {
        TableFieldEncoding.Word or TableFieldEncoding.SignedWord or TableFieldEncoding.BigEndianWord or TableFieldEncoding.BigEndianBits => 2,
        TableFieldEncoding.SignedLong or TableFieldEncoding.UnsignedLong => 4,
        TableFieldEncoding.TerminalCalendar => 6,
        TableFieldEncoding.TerminalTime => 3,
        _ => Size
    };

    public int? TextLimit => Encoding switch
    {
        TableFieldEncoding.Text or TableFieldEncoding.DisplayText or TableFieldEncoding.TerminatedDisplayText or TableFieldEncoding.MessagingKey => Size,
        TableFieldEncoding.Bcd or TableFieldEncoding.CredentialBcd => Maximum ?? Size * 2,
        TableFieldEncoding.PaddedCredentialBcd => Size * 2,
        TableFieldEncoding.CardServiceCode => 3,
        TableFieldEncoding.Dial or TableFieldEncoding.KeypadDial or TableFieldEncoding.HexBytes or TableFieldEncoding.CredentialHex => Size * 2,
        _ => null
    };

    public int LowerBound => Minimum ?? (Encoding switch
    {
        TableFieldEncoding.SignedByte => sbyte.MinValue,
        TableFieldEncoding.SignedWord => short.MinValue,
        TableFieldEncoding.SignedLong => int.MinValue,
        _ => 0
    });

    public long UpperBound => Maximum is { } max ? max : Encoding switch
    {
        TableFieldEncoding.Word or TableFieldEncoding.BigEndianWord => ushort.MaxValue,
        TableFieldEncoding.SignedByte => sbyte.MaxValue,
        TableFieldEncoding.SignedWord => short.MaxValue,
        TableFieldEncoding.SignedLong => int.MaxValue,
        TableFieldEncoding.UnsignedLong => uint.MaxValue,
        TableFieldEncoding.Bits or TableFieldEncoding.BigEndianBits or TableFieldEncoding.SmartDiscountMethod => Mask >> Shift,
        _ => byte.MaxValue
    };

    public bool IsCredential => Encoding is TableFieldEncoding.MessagingKey or TableFieldEncoding.CredentialBcd
        or TableFieldEncoding.PaddedCredentialBcd or TableFieldEncoding.CredentialHex;

    private int Shift => BitOperations.TrailingZeroCount((uint)Mask);

    public string? Read(ReadOnlySpan<byte> record)
    {
        var data = record.Slice(Offset, Width);
        return Encoding switch
        {
            TableFieldEncoding.Flag => (data[0] & Mask) != 0 ? "true" : "false",
            TableFieldEncoding.Bits or TableFieldEncoding.SmartDiscountMethod => Format((data[0] & Mask) >> Shift),
            TableFieldEncoding.Word => Format(BinaryPrimitives.ReadUInt16LittleEndian(data)),
            TableFieldEncoding.BigEndianWord => Format(BinaryPrimitives.ReadUInt16BigEndian(data)),
            TableFieldEncoding.BigEndianBits => Format((BinaryPrimitives.ReadUInt16BigEndian(data) & Mask) >> Shift),
            TableFieldEncoding.SignedByte => Format(unchecked((sbyte)data[0])),
            TableFieldEncoding.SignedWord => Format(BinaryPrimitives.ReadInt16LittleEndian(data)),
            TableFieldEncoding.SignedLong => Format(BinaryPrimitives.ReadInt32LittleEndian(data)),
            TableFieldEncoding.UnsignedLong => Format(BinaryPrimitives.ReadUInt32LittleEndian(data)),
            TableFieldEncoding.Text => ReadText(data),
            TableFieldEncoding.TerminalCalendar => TerminalClockWire.ReadCalendar(data),
            TableFieldEncoding.TerminalTime => TerminalClockWire.ReadTime(data),
            TableFieldEncoding.DisplayText => VisualPromptText.Read(data),
            TableFieldEncoding.TerminatedDisplayText => ReadTerminatedDisplayText(data),
            TableFieldEncoding.GraphicalText => GraphicalScreenText.Read(data),
            TableFieldEncoding.Bcd => ReadDigits(data, isDial: false, TextLimit ?? 0),
            TableFieldEncoding.CardServiceCode => data.IndexOfAnyExcept((byte)0) < 0 ? "" : (data[^1] & 15) == 14
                ? ReadDigits(data, isDial: false, 3) : null,
            TableFieldEncoding.Dial => ReadDigits(data, isDial: true, TextLimit ?? 0),
            TableFieldEncoding.KeypadDial => ReadDigits(data, isDial: true, TextLimit ?? 0, allowKeypad: true),
            TableFieldEncoding.Bitmap => IsBitmapScreen(record) ? Convert.ToBase64String(data) : null,
            _ when IsCredential => null,
            TableFieldEncoding.HexBytes => Convert.ToHexString(data),
            _ => Format(data[0])
        };
    }

    public void Write(Span<byte> record, string value)
    {
        if (value == Read(record))
            return;

        var data = record.Slice(Offset, Width);
        switch (Encoding)
        {
            case TableFieldEncoding.Flag:
                if (value is not ("true" or "false"))
                    throw Invalid("choose on or off");
                data[0] = (byte)((data[0] & ~Mask) | (value == "true" ? Mask : 0));
                return;
            case TableFieldEncoding.Text:
                if (value.Length > Size || value.Any(ch => ch is < ' ' or > '~'))
                    throw Invalid($"enter up to {Size} printable characters");
                data.Fill((byte)' ');
                System.Text.Encoding.ASCII.GetBytes(value, data);
                return;
            case TableFieldEncoding.TerminalCalendar:
                if (!TerminalClockWire.TryWriteCalendar(value, data))
                    throw Invalid("enter a date and time within the terminal clock range");
                return;
            case TableFieldEncoding.TerminalTime:
                if (!TerminalClockWire.TryWriteTime(value, data))
                    throw Invalid("enter a time of day from 00:00:00 through 23:59:59");
                return;
            case TableFieldEncoding.DisplayText:
            case TableFieldEncoding.TerminatedDisplayText:
                if (!VisualPromptText.TryWrite(value, data, Encoding == TableFieldEncoding.TerminatedDisplayText ? (byte)0 : (byte)' '))
                    throw Invalid($"enter up to {Size} display characters using ASCII or supported accents");
                return;
            case TableFieldEncoding.GraphicalText:
                if (!GraphicalScreenText.TryWrite(value, data))
                    throw Invalid("enter up to 54 lines of 19 supported display characters");
                return;
            case TableFieldEncoding.Bcd:
            case TableFieldEncoding.Dial:
            case TableFieldEncoding.KeypadDial:
                WriteDigits(data, value, Encoding != TableFieldEncoding.Bcd, Encoding == TableFieldEncoding.KeypadDial);
                return;
            case TableFieldEncoding.CredentialBcd:
                WriteDigits(data, value, isDial: false, allowKeypad: false);
                if ((value.Length & 1) != 0)
                    data[^1] = (byte)((data[^1] & 0xF0) | 0x0E);
                return;
            case TableFieldEncoding.PaddedCredentialBcd:
                if (value.Length == 0)
                    return;
                if (value == "clear")
                {
                    data.Fill(0xEE);
                    return;
                }
                if (value.Length > Size * 2 || value.Any(ch => !char.IsAsciiDigit(ch)))
                    throw Invalid($"enter up to {Size * 2} decimal digits");
                data.Fill(0xEE);
                for (var index = 0; index < value.Length; index++)
                {
                    var shift = index % 2 == 0 ? 4 : 0;
                    var offset = index / 2;
                    data[offset] = (byte)((data[offset] & ~(15 << shift)) | ((value[index] - '0') << shift));
                }
                return;
            case TableFieldEncoding.CardServiceCode:
                if (value.Length == 0)
                    data.Clear();
                else
                {
                    WriteDigits(data, value, isDial: false, allowKeypad: false);
                    data[^1] = (byte)((data[^1] & 0xF0) | 0x0E);
                }
                return;
            case TableFieldEncoding.HexBytes:
            case TableFieldEncoding.CredentialHex:
                if (Encoding == TableFieldEncoding.CredentialHex && value.Length == 0)
                    return;
                if (value.Length != Size * 2 || !value.All(Uri.IsHexDigit))
                    throw Invalid($"enter exactly {Size * 2} hexadecimal characters");
                Convert.FromHexString(value).CopyTo(data);
                return;
            case TableFieldEncoding.MessagingKey:
                if (value.Length != Size || value.Any(ch => !char.IsAsciiDigit(ch)) || value.StartsWith("0000", StringComparison.Ordinal))
                    throw Invalid($"enter {Size} digits with a nonzero first four-digit block");
                System.Text.Encoding.ASCII.GetBytes(value, data);
                return;
            case TableFieldEncoding.Bitmap:
                if (!IsBitmapScreen(record))
                    throw Invalid("select a bitmap screen");
                if (!Convert.TryFromBase64String(value, data, out var written) || written != data.Length)
                    throw Invalid("upload a complete 112 by 72 pixel bitmap");
                return;
        }

        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            || number < LowerBound || number > UpperBound)
            throw Invalid($"enter a whole number from {LowerBound} to {UpperBound}");
        if (Choices != null && !Choices.Any(choice => choice.Value == number))
            throw Invalid("choose a listed value");

        switch (Encoding)
        {
            case TableFieldEncoding.Word:
                BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)number);
                break;
            case TableFieldEncoding.BigEndianWord:
                BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)number);
                break;
            case TableFieldEncoding.BigEndianBits:
                var current = BinaryPrimitives.ReadUInt16BigEndian(data);
                BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)((current & ~Mask) | ((int)number << Shift)));
                break;
            case TableFieldEncoding.SignedByte:
                data[0] = unchecked((byte)(sbyte)number);
                break;
            case TableFieldEncoding.SignedWord:
                BinaryPrimitives.WriteInt16LittleEndian(data, (short)number);
                break;
            case TableFieldEncoding.SignedLong:
                BinaryPrimitives.WriteInt32LittleEndian(data, (int)number);
                break;
            case TableFieldEncoding.UnsignedLong:
                BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)number);
                break;
            case TableFieldEncoding.Bits:
            case TableFieldEncoding.SmartDiscountMethod:
                data[0] = (byte)((data[0] & ~Mask) | ((int)number << Shift));
                break;
            default:
                data[0] = (byte)number;
                break;
        }
    }

    private void WriteDigits(Span<byte> data, string value, bool isDial, bool allowKeypad)
    {
        var limit = TextLimit ?? 0;
        if ((!isDial && value.Length != limit) || value.Length > limit
            || value.Any(ch => !char.IsAsciiDigit(ch) && !(allowKeypad && ch is '*' or '#')))
            throw Invalid(allowKeypad ? $"enter up to {limit} keypad digits (0 to 9, * or #)"
                : isDial ? $"enter up to {limit} decimal digits" : $"enter exactly {limit} decimal digits");

        var finalNibble = data[^1] & 15;
        data.Clear();
        for (var i = 0; i < value.Length; i++)
        {
            var digit = value[i] switch { '0' when isDial => 10, '*' => 11, '#' => 12, var ch => ch - '0' };
            data[i / 2] |= (byte)(digit << (i % 2 == 0 ? 4 : 0));
        }
        if (!isDial && limit % 2 != 0)
            data[^1] |= (byte)(finalNibble is 14 or 15 ? finalNibble : 14);
    }

    private static string? ReadDigits(ReadOnlySpan<byte> data, bool isDial, int limit, bool allowKeypad = false)
    {
        Span<char> output = stackalloc char[limit];
        for (var i = 0; i < limit; i++)
        {
            var digit = (data[i / 2] >> (i % 2 == 0 ? 4 : 0)) & 15;
            if (isDial && digit == 0)
                return new string(output[..i]);
            if (isDial && digit == 10)
                digit = 0;
            if (allowKeypad && digit is 11 or 12)
            {
                output[i] = digit == 11 ? '*' : '#';
                continue;
            }
            if (digit > 9)
                return null;
            output[i] = (char)('0' + digit);
        }
        return new string(output);
    }

    private static string? ReadText(ReadOnlySpan<byte> data)
    {
        var value = System.Text.Encoding.ASCII.GetString(data).TrimEnd(' ', '\0');
        return value.Any(ch => ch is < ' ' or > '~') || data.IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0
            ? null : value;
    }

    private static string? ReadTerminatedDisplayText(ReadOnlySpan<byte> data)
    {
        var terminator = data.IndexOf((byte)0);
        return VisualPromptText.Read(terminator < 0 ? data : data[..terminator]);
    }

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    private bool IsBitmapScreen(ReadOnlySpan<byte> record) => BitmapTypeOffset is { } offset && record[offset] == (byte)'B';

    private InvalidOperationException Invalid(string detail) => new($"{Label}: {detail}.");

    internal static bool IsUsableMessagingKey(ReadOnlySpan<byte> value) => value.Length == 8
        && !value[..4].SequenceEqual("0000"u8) && value.IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0;
}

public sealed record NativeTableSection(string Key, string Label, IReadOnlyList<NativeTableField> Fields,
    bool ShowDisplayPreview = false, int? GraphicalScreenIndex = null, bool ShowPanelPreview = false);

public sealed record NativeTableLayout(byte Number, int Length, string Profile, IReadOnlyList<NativeTableSection> Sections)
{
    public byte[] Apply(ReadOnlySpan<byte> original, IReadOnlyDictionary<string, string> changes)
    {
        if (original.Length != Length || original[0] != Number)
            throw new InvalidOperationException("The record does not match this editor's wire profile.");
        var edited = original.ToArray();
        var sections = Number switch
        {
            22 or 134 => NativeTableLayouts.PrepareCardChanges(edited, changes),
            93 => NativeTableLayouts.PrepareStoredValueChanges(edited, changes, this),
            >= 131 and <= 133 => NativeTableLayouts.PrepareGraphicalChanges(edited, changes),
            _ => Sections
        };
        var fields = sections.SelectMany(s => s.Fields).ToDictionary(f => f.Key, StringComparer.Ordinal);
        ValidateClockChanges(original, fields, changes);
        foreach (var (key, value) in changes)
        {
            if (!fields.TryGetValue(key, out var field) || value == null)
                throw new InvalidOperationException($"Unknown or empty field {key}.");
            field.Write(edited, value);
        }
        if (Number == NativeTableLayouts.MessagingRecord)
            NativeTableLayouts.ValidateMessagingChanges(original, edited);
        if (Number is 22 or 134)
            NativeTableLayouts.ValidateCardChanges(original, edited);
        if (Number is 23 or 135)
            NativeTableLayouts.ValidateCarrierChanges(original, edited);
        if (Number == CodeDownloadWire.MessageType && !original.SequenceEqual(edited) && CodeDownloadWire.ConfigurationIssue(edited) is { } issue)
            throw new InvalidOperationException(issue);
        return edited;
    }

    private static void ValidateClockChanges(ReadOnlySpan<byte> original, Dictionary<string, NativeTableField> fields,
        IReadOnlyDictionary<string, string> changes)
    {
        foreach (var clock in fields.Values)
        {
            if (clock.Encoding is not (TableFieldEncoding.TerminalCalendar or TableFieldEncoding.TerminalTime)
                || !changes.TryGetValue(clock.Key, out var time))
                continue;
            foreach (var (key, value) in changes)
            {
                if (key == clock.Key || !fields.TryGetValue(key, out var part)
                    || part.Offset >= clock.Offset + clock.Width || part.Offset + part.Width <= clock.Offset)
                    continue;
                if (clock.Read(original) != time || part.Read(original) != value)
                    throw new InvalidOperationException($"Change either {clock.Label} or its individual fields in this save.");
            }
        }
    }
}
