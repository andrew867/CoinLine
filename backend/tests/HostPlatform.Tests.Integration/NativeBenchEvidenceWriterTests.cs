namespace HostPlatform.Tests.Integration;

public sealed class NativeBenchEvidenceWriterTests
{
    [Fact]
    public void Temporary_sharing_errors_retry_the_same_atomic_replacement()
    {
        var attempts = 0;
        var waits = 0;
        NativeBenchEvidenceWriter.ReplaceWithRetry("staged", "snapshot", (source, target) =>
        {
            Assert.Equal("staged", source);
            Assert.Equal("snapshot", target);
            if (++attempts == 1) throw new UnauthorizedAccessException();
            if (attempts == 2) throw new IOException();
        }, () => waits++);
        Assert.Equal(3, attempts);
        Assert.Equal(2, waits);
    }

    [Fact]
    public void Permanent_error_is_not_hidden_and_retries_are_bounded()
    {
        var attempts = 0;
        var waits = 0;
        Assert.Throws<UnauthorizedAccessException>(() =>
            NativeBenchEvidenceWriter.ReplaceWithRetry("staged", "snapshot", (_, _) =>
            {
                attempts++;
                throw new UnauthorizedAccessException();
            }, () => waits++));
        Assert.Equal(NativeBenchEvidenceWriter.MaximumAttempts, attempts);
        Assert.Equal(attempts - 1, waits);
    }

    [Fact]
    public void Successful_replacement_retains_complete_new_snapshot()
    {
        var staging = Path.GetTempFileName();
        var destination = Path.GetTempFileName();
        try
        {
            File.WriteAllText(staging, "{\"state\":\"completed\"}");
            File.WriteAllText(destination, "{\"state\":\"running\"}");
            NativeBenchEvidenceWriter.ReplaceWithRetry(staging, destination);
            Assert.Equal("{\"state\":\"completed\"}", File.ReadAllText(destination));
            Assert.False(File.Exists(staging));
        }
        finally
        {
            File.Delete(staging);
            File.Delete(destination);
        }
    }
}
