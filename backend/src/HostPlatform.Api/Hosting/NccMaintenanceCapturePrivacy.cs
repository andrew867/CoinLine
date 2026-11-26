using System.Buffers.Binary;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Hosting;

internal static class NccMaintenanceCapturePrivacy
{
    // Keep offsets and non-maintenance records. A recognized complete frame is
    // rechecksummed after masking its identifier, and cannot be used to match a technician.
    public static byte[] Redact(byte[] source)
    {
        byte[]? updated = null;
        foreach (var item in NccStreamReader.ReadOrdered(source, NccParseMode.DiagnosticCapture).OfType<NccStreamParsedFrame>())
        {
            var frame = item.Frame.RawBytes;
            if (frame.Length < 9 || NccControl.IsControlPacket(frame[1]) || frame[8] != DlogMaintenanceWire.RequestMessageType) continue;
            var payloadEnd = item.Frame.IsTruncated ? frame.Length : frame.Length - 3;
            if (payloadEnd <= 11) continue;
            updated ??= source.ToArray();
            var target = updated.AsSpan(item.StartOffset, frame.Length);
            for (var offset = 8; offset < payloadEnd && target[offset] == DlogMaintenanceWire.RequestMessageType;
                offset += DlogMaintenanceWire.RequestLength)
            {
                var identifierStart = offset + 3;
                var count = Math.Min(3, payloadEnd - identifierStart);
                if (count > 0) target.Slice(identifierStart, count).Clear();
            }
            if (!item.Frame.IsTruncated && frame[^1] == NccConstants.FrameEnd &&
                BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(frame.Length - 3, 2)) == NccCrc16.Compute(frame.AsSpan(0, frame.Length - 3)))
                BinaryPrimitives.WriteUInt16LittleEndian(target[^3..], NccCrc16.Compute(target[..^3]));
        }
        return updated ?? source;
    }

    public static async Task ScrubStoredAsync(HostPlatformDbContext db, CancellationToken ct = default)
    {
        // Older operator-uploaded captures can contain data that live session
        // storage already omits. Process bounded pages, preserving capture IDs,
        // lengths and unaffected bytes.
        const int pageSize = 32;
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await db.NccFrameCaptures.OrderBy(row => row.Id).Skip(offset).Take(pageSize).ToArrayAsync(ct);
            if (page.Length == 0) return;
            foreach (var row in page)
            {
                var redacted = Redact(row.RawBytes);
                if (!redacted.AsSpan().SequenceEqual(row.RawBytes)) row.RawBytes = redacted;
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
            foreach (var row in page) db.Entry(row).State = EntityState.Detached;
            if (page.Length < pageSize) return;
        }
    }
}
