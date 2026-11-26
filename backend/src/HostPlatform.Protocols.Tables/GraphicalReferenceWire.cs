using System.Buffers.Binary;

namespace HostPlatform.Protocols.Tables;

public sealed record GraphicalRecordPresence(int Number, string Availability);
public sealed record GraphicalLinkReference(int SourceRecord, string SourceSectionKey, string SourceSection, string SourceField, string FieldKey,
    string Role, int Address, string Target, string TargetState, string? Advisory);
public sealed record GraphicalUsageScreen(int RecordNumber, int ScreenNumber, byte KindCode, ushort ScreenOwnerCode, ushort[] KeyOwnerCodes);
public sealed record GraphicalUsageConfiguration(ushort? CollectionDurationDays, GraphicalUsageScreen[] Screens);
public sealed record GraphicalLinkReview(GraphicalRecordPresence[] Records, GraphicalLinkReference[] References, GraphicalUsageConfiguration Usage);

public static partial class NativeTableLayouts
{
    /// <summary>Review stored graphical references, not execution of a particular screen or physical key.</summary>
    public static GraphicalLinkReview ReviewGraphicalLinks(IReadOnlyDictionary<int, byte[]> records,
        IReadOnlySet<int>? ambiguous = null)
    {
        var included = new Dictionary<int, byte[]>();
        var presence = new List<GraphicalRecordPresence>();
        foreach (var number in new[] { 131, 132, 133 })
        {
            var availability = ambiguous?.Contains(number) == true ? "ambiguous"
                : !records.TryGetValue(number, out var candidate) ? "missing"
                : candidate.Length != GraphicalRecordLength || candidate[0] != number ? "incompatible" : "included";
            presence.Add(new(number, availability));
            if (availability == "included")
                included.Add(number, records[number]);
        }

        var links = new List<GraphicalLinkReference>();
        if (included.TryGetValue(131, out var header))
        {
            if ((header[3] & 1) != 0)
                Add(131, "rotation", "Graphical rotation", "On-hook rotation start", "onHook", "enabled_context", 0);
            HeaderLink(2, 4, "Off-hook entry screen", "offHookScreen");
            HeaderLink(4, 5, "Connected-call entry screen", "connectedScreen");

            void HeaderLink(byte mask, int offset, string label, string key)
            {
                var enabled = (header[3] & mask) != 0;
                if (enabled || header[offset] != NoScreen)
                    Add(131, "rotation", "Graphical rotation", label, key, enabled ? "enabled_context" : "disabled_context", header[offset]);
            }
        }

        foreach (var (number, data) in included.OrderBy(row => row.Key))
        {
            var sections = GraphicalSections(data);
            foreach (var section in sections.Where(section => section.GraphicalScreenIndex.HasValue))
            {
                var index = section.GraphicalScreenIndex!.Value;
                var kind = data[ScreenOffset(index)];
                if (kind is not (BitmapScreen or TextScreen or MixedScreen))
                    continue;
                // Main-screen controls and keys in a mixed screen are stored
                // defaults; their execution must not be inferred from a panel link.
                var role = kind == MixedScreen && !section.Key.Contains(".panel-", StringComparison.Ordinal)
                    ? "mixed_default" : "configured_reference";
                foreach (var field in section.Fields.Where(field => field.Encoding == TableFieldEncoding.GraphicalLink))
                {
                    var address = data[field.Offset];
                    if (address != NoScreen)
                        Add(number, section.Key, section.Label, field.Label, field.Key, role, address);
                }
            }
        }
        return new(presence.ToArray(), links.ToArray(), ReadUsageConfiguration(included));

        void Add(int source, string sectionKey, string section, string field, string key, string role, int address)
        {
            var (target, state, advisory) = Resolve(address, role);
            links.Add(new(source, sectionKey, section, field, key, role, address, target, state, advisory));
        }

        (string Target, string State, string? Advisory) Resolve(int address, string role)
        {
            if (address == NoScreen)
                return ("No screen", "no_selection", role == "enabled_context"
                    ? "The enabled display context has no selected entry screen." : null);
            var screen = address & 63;
            var panel = address >> 6;
            if (screen == 63)
                return ("Built-in application screen", panel == 0 ? "builtin" : "noncanonical_builtin",
                    panel == 0 ? null : "The built-in application reference includes nonzero panel bits. Its behavior is unverified.");
            if (screen >= 24)
                return ($"Stored address {address}", "unrecognized_address", "This stored address is not a recognized graphical screen.");
            var targetNumber = 131 + screen / ScreensPerRecord;
            var label = $"Screen {screen + 1}";
            if (!included.TryGetValue(targetNumber, out var target))
            {
                var availability = presence.Single(row => row.Number == targetNumber).Availability;
                return (panel == 0 ? label : $"{label}, panel {panel + 1}", availability + "_record",
                    availability == "missing" ? $"MT{targetNumber} is not in this set. A partial download may retain the terminal's existing record."
                    : $"MT{targetNumber} cannot be reviewed because its saved record is {availability}.");
            }
            var kind = target[ScreenOffset(screen % ScreensPerRecord)];
            if (kind == 0)
                return (label, "undefined_screen", "The target screen is undefined in the included record.");
            if (kind is not (BitmapScreen or TextScreen or MixedScreen))
                return (label, "unknown_screen_kind", "The target screen type is unrecognized.");
            if (kind == MixedScreen)
                return ($"{label}, panel {panel + 1}", "resolved", null);
            if (panel != 0)
                return ($"{label}, panel {panel + 1}", "non_mixed_panel",
                    "The target is not a mixed screen, but the reference has nonzero panel bits. Verify terminal behavior.");
            return (label, "resolved", null);
        }
    }

    private static GraphicalUsageConfiguration ReadUsageConfiguration(IReadOnlyDictionary<int, byte[]> included)
    {
        // Only the MT131 header controls this reporting period. Do not use the
        // other stored headers as substitutes when MT131 is absent.
        ushort? duration = included.TryGetValue(131, out var header)
            ? BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(1)) : null;
        var screens = new List<GraphicalUsageScreen>();
        foreach (var (number, record) in included.OrderBy(row => row.Key))
        {
            for (var slot = 0; slot < ScreensPerRecord; slot++)
            {
                var offset = ScreenOffset(slot);
                var owners = new ushort[ButtonsPerScreen];
                for (var button = 0; button < owners.Length; button++)
                    owners[button] = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + ScreenButtons + button * ButtonSize));
                // A mixed screen's panel usage accumulates under main-screen
                // ownership and the six main key positions, not panel owners.
                screens.Add(new(number, (number - 131) * ScreensPerRecord + slot + 1, record[offset],
                    BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + ScreenAttributes + 11)), owners));
            }
        }
        return new(duration, screens.ToArray());
    }
}
