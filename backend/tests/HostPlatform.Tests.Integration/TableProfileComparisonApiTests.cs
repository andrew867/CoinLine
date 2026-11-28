using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class TableProfileComparisonApiTests(ApiFixture factory)
{
    [Fact]
    public async Task Published_set_comparison_is_read_only_and_uses_both_payload_storage_formats()
    {
        var featureDefinition = new TableDefinition { Name = "Synthetic features", TableNumber = 26 };
        var screenDefinition = new TableDefinition { Name = "Synthetic screening", TableNumber = 92 };
        var raw = new byte[72]; raw[0] = 26; raw[^1] = 0xA5;
        var screening = new byte[3061]; screening[0] = 92;
        var normalized = new TablePayload { RawContent = raw, LengthBytes = raw.Length, Sha256Hex = TablePayloadHasher.Sha256Hex(raw) };
        var set = new TableSet { Name = "Synthetic published configuration", Status = TableSetStatus.Published, PublishGeneration = 3 };
        var feature = new TableVersion
        {
            TableDefinitionId = featureDefinition.Id, TablePayloadId = normalized.Id, TableRevision = 1,
            PayloadSha256Hex = normalized.Sha256Hex, ScreeningLayoutProfile = NativeTableLayouts.Later
        };
        var screen = new TableVersion
        {
            TableDefinitionId = screenDefinition.Id, EmbeddedPayload = screening, TableRevision = 2,
            PayloadSha256Hex = TablePayloadHasher.Sha256Hex(screening)
        };
        set.Versions.Add(feature); set.Versions.Add(screen);
        var firmware = new FirmwareVersion { Label = "Synthetic registered target", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var targeted = new TableSet { Name = "Synthetic targeted draft", TargetFirmwareVersionId = firmware.Id };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(featureDefinition, screenDefinition, normalized, set, firmware, targeted);
            await db.SaveChangesAsync();
        }

        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set.Id}");
        var comparisons = detail.GetProperty("profileComparison").EnumerateArray().ToArray();
        var candidate = Assert.Single(comparisons, row => row.GetProperty("profileId").GetString() == FirmwareTableProfiles.GraphicalMultiPay180);
        Assert.Equal(2, candidate.GetProperty("matchingLengthCount").GetInt32());
        Assert.Equal(0, candidate.GetProperty("conflictCount").GetInt32());
        var earlier = Assert.Single(comparisons, row => row.GetProperty("profileId").GetString() == FirmwareTableProfiles.MultiPay200);
        Assert.Equal(2, earlier.GetProperty("conflictCount").GetInt32());
        foreach (var record in candidate.GetProperty("records").EnumerateArray())
        {
            Assert.False(record.TryGetProperty("rawContent", out _));
            Assert.False(record.TryGetProperty("embeddedPayload", out _));
        }

        var targetedDetail = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{targeted.Id}");
        Assert.True(!targetedDetail.TryGetProperty("profileComparison", out var comparison) || comparison.ValueKind == JsonValueKind.Null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var retained = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TablePayload)
                .SingleAsync(row => row.Id == set.Id);
            Assert.Null(retained.TargetFirmwareVersionId);
            Assert.Equal(TableSetStatus.Published, retained.Status);
            Assert.Equal(3, retained.PublishGeneration);
            Assert.Equal(raw, TableDistributionService.GetEffectivePayload(retained.Versions.Single(row => row.Id == feature.Id)));
            Assert.Equal(screening, TableDistributionService.GetEffectivePayload(retained.Versions.Single(row => row.Id == screen.Id)));
        }
    }
}
