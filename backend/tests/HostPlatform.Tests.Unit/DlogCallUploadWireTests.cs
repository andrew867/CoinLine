using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Unit;

public sealed class DlogCallUploadWireTests
{
    [Fact]
    public void Packed_call_and_statistics_records_are_split_without_reading_end_markers_inside_records()
    {
        byte[] payload = [
            .. Record(53, 54), .. Record(53, 54), .. Record(56, 81),
            .. Record(10, 11), .. Record(60, 28)];
        Assert.Equal(228, payload.Length);
        Assert.True(DlogCallUploadWire.TryParse(payload, out var parsed));
        Assert.False(parsed!.EndsDialogue);
        Assert.Equal(new[] { 0..54, 54..108 }, parsed.CallDetails);
        Assert.Equal(new[] { 108..189 }, parsed.Statistics);
        Assert.Equal(new[] { 189..200, 200..228 }, parsed.Status);
        Assert.Empty(parsed.Cashbox);
        Assert.Equal(new[] { (53, 54), (53, 54), (56, 81), (10, 11), (60, 28) },
            parsed.Records.Select(record => ((int)record.MessageType, record.ByteCount)));
        Assert.True(DlogCallUploadWire.TryParse([.. Record(37, 99), 13], out parsed));
        Assert.True(parsed!.EndsDialogue);
        Assert.Equal(new[] { 0..99 }, parsed.Statistics);
    }

    [Fact]
    public void Empty_upload_status_trailer_and_one_frame_call_upload_are_recognized()
    {
        Assert.True(DlogCallUploadWire.TryParse([.. Record(10, 11), .. Record(60, 28), 13], out var status));
        Assert.True(status!.EndsDialogue);
        Assert.Empty(status.CallDetails);
        Assert.Equal(2, status.Status.Count);
        Assert.True(DlogCallUploadWire.TryParse([.. Record(61, 35), .. Record(38, 57), 13], out var mixed));
        Assert.True(mixed!.EndsDialogue);
        Assert.Equal(new[] { 0..35 }, mixed.CallDetails);
        Assert.Equal(new[] { 35..92 }, mixed.Cashbox);
        Assert.Equal(new[] { (61, 35), (38, 57), (13, 1) },
            mixed.Records.Select(record => ((int)record.MessageType, record.ByteCount)));
        Assert.True(DlogCallUploadWire.TryParse([13], out var end));
        Assert.True(end!.EndsDialogue);
    }

    [Fact]
    public void Unknown_truncated_or_trailing_data_never_qualifies_for_completion()
    {
        foreach (var invalid in new byte[][]
        {
            [], [13, 13], [84, 13], [.. Record(53, 54)[..^1]], [.. Record(10, 11), 60],
            [.. Record(37, 99), 13, 42], [.. Record(57, 190)[..^1]]
        })
        {
            Assert.False(DlogCallUploadWire.TryParse(invalid, out var frame));
            Assert.Null(frame);
        }
    }

    private static byte[] Record(byte type, int length)
    {
        var data = new byte[length];
        data.AsSpan().Fill(13);
        data[0] = type;
        return data;
    }
}
