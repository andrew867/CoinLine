using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record RoutineCallInRandomization(string Mode, string? WindowStart = null, string? WindowEnd = null, Guid? Seed = null);

/// <summary>Stable per-phone local-clock time choices for a reviewed MT35 rollout.</summary>
public sealed class RoutineCallInRandomizationPlan
{
    private const string TimeFormat = "HH:mm:ss";
    private readonly TableVersion _original;
    private readonly byte[] _raw;
    private readonly Guid _seed;

    private RoutineCallInRandomizationPlan(TableVersion original, byte[] raw, Guid seed, TimeOnly start, TimeOnly end)
    {
        _original = original;
        _raw = raw;
        _seed = seed;
        Start = start;
        End = end;
    }

    public TimeOnly Start { get; }
    public TimeOnly End { get; }

    public static RoutineCallInRandomizationPlan Create(TableSet source, RoutineCallInRandomization request)
    {
        if (request.Seed is not { } seed || seed == Guid.Empty)
            throw new InvalidOperationException("Preview the random call-in times before applying them.");
        var candidates = source.Versions.Where(row => row.TableDefinition?.TableNumber == NativeTableLayouts.CallInRecord).Take(2).ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException("Choose exactly one source MT35 record.");
        var original = candidates[0];
        var raw = TableDistributionService.GetEffectivePayload(original);
        if (raw.Length != 21 || raw[0] != NativeTableLayouts.CallInRecord)
            throw new InvalidOperationException("Randomizing call-in times requires the verified 21-byte MT35 record.");

        TimeOnly start;
        TimeOnly end;
        switch (request.Mode)
        {
            case "source-time" when request.WindowStart == null && request.WindowEnd == null:
                if (raw[4] > 23 || raw[5] > 59 || raw[6] > 59)
                    throw new InvalidOperationException("The source MT35 has an invalid time. Use an explicit time window.");
                start = TimeOnly.MinValue;
                end = new TimeOnly(raw[4], raw[5], raw[6]);
                break;
            case "window":
                if (!ParseTime(request.WindowStart, out start) || !ParseTime(request.WindowEnd, out end) || start > end)
                    throw new InvalidOperationException("Choose a valid same-day call-in time window with start at or before end.");
                break;
            default:
                throw new InvalidOperationException("Choose the source time or an explicit call-in time window.");
        }
        return new(original, raw, seed, start, end);
    }

    public string TimeFor(Guid phoneId)
    {
        // A stored rollout seed and immutable phone ID make every preview,
        // confirmation and paused retry select the same time independently.
        Span<byte> input = stackalloc byte[32];
        _seed.TryWriteBytes(input[..16]);
        phoneId.TryWriteBytes(input[16..]);
        var digest = SHA256.HashData(input);
        var seconds = (int)(End - Start).TotalSeconds + 1;
        var offset = (int)(BinaryPrimitives.ReadUInt64LittleEndian(digest) % (ulong)seconds);
        return Start.Add(TimeSpan.FromSeconds(offset)).ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    public TableSet SourceFor(TableSet source, string time)
    {
        var revised = NativeTableLayouts.Find(NativeTableLayouts.CallInRecord, _raw)!
            .Apply(_raw, new Dictionary<string, string> { ["startTime"] = time });
        if (revised.AsSpan().SequenceEqual(_raw))
            return source;
        var record = TableDistributionService.CopyVersion(_original);
        record.TableDefinition = _original.TableDefinition;
        record.TablePayload = null;
        record.TablePayloadId = null;
        record.EmbeddedPayload = revised;
        record.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(revised);
        record.Checksum = SHA256.HashData(revised);
        (record.ValidationPassed, record.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(_original.TableDefinition!, revised);

        return new TableSet
        {
            Id = source.Id, Name = source.Name, CustomerId = source.CustomerId,
            TargetFirmwareVersionId = source.TargetFirmwareVersionId, TargetFirmwareVersion = source.TargetFirmwareVersion,
            Status = source.Status, Versions = source.Versions.Select(version => version == _original ? record : version).ToList()
        };
    }

    private static bool ParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, [TimeFormat, "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
