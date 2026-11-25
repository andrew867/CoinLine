namespace HostPlatform.Protocols.Tables;

public sealed record RestrictedRoute(int Slot, string? Destination, int Classification, string? ClassificationLabel,
    bool Dialable, string Status, int? TrailingWildcards = null, bool? Translated = null);

public sealed record RestrictedRouteRecord(int Number, string Availability, RestrictedRoute[] Routes);

public static class ServiceRestrictionWire
{
    public const byte MessageType = 58;
    public const int RecordLength = 26;
    public const int ConfigurableAccess = 4;

    public static string? ConfigurationIssue(ReadOnlySpan<byte> record, bool later)
    {
        if (record.Length != RecordLength || record[0] != MessageType)
            return "Import a complete 26-byte service restriction record.";
        if (record[1] == 0)
            return null;
        if (record[1] > (later ? ConfigurableAccess : 3))
            return $"Restriction level {record[1]} is not supported by this firmware profile.";
        if (record[2] > 23 || record[3] > 59)
            return "Choose a restriction start time from 00:00 to 23:59.";
        if (record[4] > 24 || record[5] > 59 || (record[4] == 24 && record[5] != 0))
            return "Choose a daily restriction duration of at most 24 hours.";
        if (record[4] == 0 && record[5] == 0)
            return "An enabled restriction requires a positive duration, or select no restrictions.";
        return null;
    }

    public static bool HasPermittedScreeningSlot(ReadOnlySpan<byte> record) =>
        ReadRestrictedRoutes(92, record).Routes.Any(route => route.Dialable);

    public static bool HasPermittedQuickAccessKey(ReadOnlySpan<byte> record) =>
        ReadRestrictedRoutes(55, record).Routes.Any(route => route.Dialable);

    // Return the exact marked entries, including unusable marks. This is an
    // advisory review of saved table bytes, not a terminal call simulation.
    public static RestrictedRouteRecord ReadRestrictedRoutes(int number, ReadOnlySpan<byte> record)
    {
        var rowLength = number == 92 ? CallScreeningWire.ExpandedSlotLength : number == 55 ? QuickAccessKeyWire.SlotLength : 0;
        var length = number == 92 ? 3061 : number == 55 ? QuickAccessKeyWire.RecordLength : 0;
        if (rowLength == 0 || record.Length != length || record[0] != number
            || NativeTableLayouts.Find(number, record, NativeTableLayouts.Later) is not { } layout)
            return new(number, "incompatible", []);

        var routes = new List<RestrictedRoute>();
        var screening = number == 92;
        for (int offset = 1, slot = 1; offset < record.Length; offset += rowLength, slot++)
        {
            var row = record.Slice(offset, rowLength);
            if ((row[screening ? 13 : 55] & (screening ? 4 : 1)) == 0)
                continue;
            var classification = row[1] & 15;
            var section = layout.Sections[slot - 1];
            var destinationField = section.Fields[0];
            var classificationField = section.Fields.Single(field =>
                field.Key.EndsWith(screening ? ".classification" : ".type", StringComparison.Ordinal));
            var destination = destinationField.Read(record);
            var hasNumber = (row[screening ? 4 : 3] >> 4) != 0;
            var status = !screening && (row[0] & QuickAccessKeyWire.Information) != 0 ? "information_only"
                : classification == 11 ? "denied" : !hasNumber ? "blank"
                : destination == null ? "unreadable_number" : screening && row[3] > 15 ? "unreadable_wildcards"
                : "configured_dialable";
            routes.Add(new(slot, destination, classification,
                classificationField.Choices?.FirstOrDefault(choice => choice.Value == classification)?.Label,
                status == "configured_dialable", status, screening ? row[3] : null,
                screening ? (row[13] & 0x40) != 0 : null));
        }
        return new(number, "included", routes.ToArray());
    }

}

public static partial class NativeTableLayouts
{
    private static NativeTableLayout ServiceRestrictionLayout(bool later) => new(ServiceRestrictionWire.MessageType,
        ServiceRestrictionWire.RecordLength, later ? Later : MultiPay,
        [Section("restriction", "Daily service restriction", [
            Select("restriction", "Restriction level", 1, [(0, "None"), (1, "Free and emergency calls only"),
                (2, "No coin or smart-card calls"), (3, "Restrict in-call keypad signaling in coin or smart-card mode"),
                .. later ? new[] { (ServiceRestrictionWire.ConfigurableAccess, "Configurable restricted access") } : []]),
            Hour("startHour", "Start hour (phone local time)", 2), Minute("startMinute", "Start minute", 3),
            Field("durationHours", "Duration hours", 4, max: 24), Minute("durationMinutes", "Duration minutes", 5),
            .. SpareBytes("spare", 6, 20)])]);
}
