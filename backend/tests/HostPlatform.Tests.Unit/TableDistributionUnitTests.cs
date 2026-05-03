using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TableDistributionUnitTests
{
    [Fact]
    public void Payload_sha256_is_deterministic_and_lowercase_hex()
    {
        var raw = new byte[] { 1, 2, 3, 4, 5 };
        var a = TablePayloadHasher.Sha256Hex(raw);
        var b = TablePayloadHasher.Sha256Hex(raw);
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.Equal(a, a.ToLowerInvariant());
    }

    [Fact]
    public void OrderVersionsForDownload_respects_dependency_order()
    {
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var v1 = new TableVersion { Id = Guid.NewGuid(), TableDefinitionId = d1, SortOrder = 1 };
        var v2 = new TableVersion { Id = Guid.NewGuid(), TableDefinitionId = d2, SortOrder = 0, DependsOnTableDefinitionId = d1 };
        var ordered = TableDistributionService.OrderVersionsForDownload(new[] { v2, v1 });
        Assert.Equal(d1, ordered[0].TableDefinitionId);
        Assert.Equal(d2, ordered[1].TableDefinitionId);
    }

    [Fact]
    public void OrderVersionsForDownload_throws_on_cycle()
    {
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var v1 = new TableVersion { Id = Guid.NewGuid(), TableDefinitionId = d1, DependsOnTableDefinitionId = d2 };
        var v2 = new TableVersion { Id = Guid.NewGuid(), TableDefinitionId = d2, DependsOnTableDefinitionId = d1 };
        Assert.Throws<InvalidOperationException>(() =>
            TableDistributionService.OrderVersionsForDownload(new[] { v1, v2 }));
    }

    [Fact]
    public void ValidatePayload_empty_bytes_fails()
    {
        var def = new TableDefinition { Name = "x", TableNumber = 1 };
        var (ok, json) = TableDistributionService.ValidatePayload(def, Array.Empty<byte>());
        Assert.False(ok);
        Assert.Contains("EMPTY_PAYLOAD", json ?? "");
    }
}
