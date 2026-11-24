namespace HostPlatform.Protocols.Dlog;

/// <summary>Message types an operator should see as alarms, cash, or phone status. Coin counts stay in the raw bytes.</summary>
public static class FieldEventCatalog
{
    public static bool TryDescribe(int messageType, out string title)
    {
        title = messageType switch
        {
            7 => "Alarm",
            10 => "Phone status",
            16 => "Alarm acknowledgement",
            38 => "Cash box status",
            39 => "Attention callback",
            40 => "Attention required",
            41 => "Attention required",
            42 => "Attention required (CDR upload)",
            43 => "Attention required",
            44 => "Attention required (tables)",
            45 => "Attention required",
            46 => "Attention required",
            47 => "Attention required",
            51 => "Cash box collection",
            83 => "Phone error",
            _ => ""
        };
        return title.Length > 0;
    }

    public static int[] MessageTypes { get; } =
        [7, 10, 16, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 51, 83];
}
