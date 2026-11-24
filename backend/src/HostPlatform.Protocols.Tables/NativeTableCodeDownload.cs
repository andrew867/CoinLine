using static HostPlatform.Protocols.Tables.TableFieldEncoding;

namespace HostPlatform.Protocols.Tables;

public static partial class NativeTableLayouts
{
    private static NativeTableSection[] CodeDownloadSections() =>
    [
        Section("schedule", "Code download schedule",
            [Toggle("enabled", "Enable the scheduled firmware download", 1, 1),
             new NativeTableField("startAt", "Terminal start time (UTC)", CodeDownloadWire.CalendarOffset, TerminalCalendar,
                 CodeDownloadWire.CalendarLength, Help: "Set a time after the phone can pick up these parameters at a call-in."),
             Field("start.year", "Calendar year offset from 1900", 2), .. DateTimeFields("start", 2).Skip(1),
             CodeDownloadWire.CodeServer,
             Select("modemSpeed", "Code server modem speed", 18, (1, "1200 baud"), (2, "2400 baud")),
             CodeDownloadWire.TargetRom, CodeDownloadWire.TargetApp,
             new NativeTableField("serverAuthentication", "Code server authentication", 68, CredentialHex, 6,
                 Help: "Enter the 12 hexadecimal characters supplied by the code server. Leave blank to keep the stored value.")]),
        Section("retries", "Retries and alarms",
            Field("accessMinutes", "Access retry minutes", 30, max: 99), Minute("accessSeconds", "Access retry seconds", 31),
            Field("accessAlarm", "Access failures before alarm", 32),
            Field("transferMinutes", "Transfer retry minutes", 33, max: 99), Minute("transferSeconds", "Transfer retry seconds", 34),
            Field("transferAlarm", "Transfer failures before alarm", 35)),
        .. Enumerable.Range(0, 8).Select(group =>
            Section($"sector-group-{group}", $"Flash sectors {group * 32} through {group * 32 + 31}",
                Enumerable.Range(group * 32, 32).Select(sector =>
                    Toggle($"sector-{sector}", $"Request sector {sector}", 36 + sector / 8, 1 << (sector % 8))).ToArray())),
        Section("reserved", "Reserved code-download bytes",
            new NativeTableField("spareParameters", "Encoded spare parameters", 74, HexBytes, 26))
    ];

    private static void InitializeCodeDownloadRecord(Span<byte> record)
    {
        record[18] = 2;
        record[30] = 15;
        record[32] = 5;
        record[33] = 5;
        record[35] = 10;
    }
}
