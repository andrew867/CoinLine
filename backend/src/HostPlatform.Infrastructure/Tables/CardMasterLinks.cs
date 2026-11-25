using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record CardMasterLink(Guid DefinitionId, int Revision, string Code, string RowSha256);

public static class CardMasterLinks
{
    public static SortedDictionary<int, CardMasterLink> Read(string? json) =>
        json == null ? [] : JsonSerializer.Deserialize<SortedDictionary<int, CardMasterLink>>(json) ?? [];

    public static string? Write(SortedDictionary<int, CardMasterLink> links) => links.Count == 0 ? null : JsonSerializer.Serialize(links);

    public static CardMasterLink Create(CardMasterDefinition definition, int number, ReadOnlySpan<byte> record, int position) =>
        new(definition.Id, definition.Version, definition.Code, Hash(CardMasterWire.Extract(number, record, position)));

    public static bool Matches(CardMasterLink link, int number, ReadOnlySpan<byte> record, int position) =>
        string.Equals(link.RowSha256, Hash(CardMasterWire.Extract(number, record, position)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Direct native edits or replacement imports detach only modified linked rows. Unrelated rows retain their provenance.</summary>
    public static string? Reconcile(int number, ReadOnlySpan<byte> record, string? json)
    {
        if (json == null || !CardSequenceWire.Supports(number)) return null;
        var links = Read(json);
        CardSequenceWire.Slot[] slots;
        try { slots = CardSequenceWire.Read(number, record); }
        catch (InvalidOperationException) { return null; }
        foreach (var (position, link) in links.ToArray())
            if (position < 1 || position > slots.Length || !slots[position - 1].Movable || !Matches(link, number, record, position))
                links.Remove(position);
        return Write(links);
    }

    public static string? Move(int number, ReadOnlySpan<byte> updated, string? json, int from, int to)
    {
        var links = Read(json);
        if (links.Remove(from, out var link)) links[to] = link;
        else links.Remove(to);
        return Reconcile(number, updated, Write(links));
    }

    private static string Hash(byte[] row) => Convert.ToHexString(SHA256.HashData(row)).ToLowerInvariant();
}
