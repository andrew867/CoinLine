using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresInmateAssignmentTests
{
    [PostgresIntegrationFact]
    public async Task Named_inmate_references_are_versioned_and_do_not_create_unverified_downloads()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var inmate = new Terminal { Site = site, DisplayName = "Fixture inmate phone", TerminalFamilyId = "msr17-inmate",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var other = new Terminal { Site = site, DisplayName = "Fixture phone", TerminalFamilyId = "mtr19-multipay",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.Terminals.AddRange(inmate, other);
            await db.SaveChangesAsync();
        }

        var name = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var created = await client.PostAsJsonAsync("/api/inmate-features", new { name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var feature = await created.Content.ReadFromJsonAsync<JsonElement>();
        var featureId = feature.GetProperty("id").GetGuid();
        var featureRevision = feature.GetProperty("revision").GetInt32();
        var featurePath = $"/api/inmate-features/{featureId}";
        var path = $"/api/terminals/{inmate.Id}/inmate-configuration";
        var otherPath = $"/api/terminals/{other.Id}/inmate-configuration";

        var initial = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.True(initial.GetProperty("eligibleFamily").GetBoolean());
        Assert.False(initial.GetProperty("wireFormatVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("revision").ValueKind);
        Assert.Contains(initial.GetProperty("featureConfigurations").EnumerateArray(),
            option => option.GetProperty("id").GetGuid() == featureId);
        var ordinary = await client.GetFromJsonAsync<JsonElement>(otherPath);
        Assert.False(ordinary.GetProperty("eligibleFamily").GetBoolean());
        Assert.Empty(ordinary.GetProperty("featureConfigurations").EnumerateArray());

        var request = new { featureConfigurationId = featureId, setupConfigurationId = "lab01 ", revision = (int?)null };
        using (var wrongFamily = await client.PutAsJsonAsync(otherPath, request))
            Assert.Equal(HttpStatusCode.BadRequest, wrongFamily.StatusCode);
        using (var unknown = await client.PutAsJsonAsync(path, new { featureConfigurationId = Guid.NewGuid(),
            setupConfigurationId = "LAB01", revision = (int?)null }))
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using (var invalid = await client.PutAsJsonAsync(path, new { featureConfigurationId = featureId,
            setupConfigurationId = "TOOLONG", revision = (int?)null }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var saved = await client.PutAsJsonAsync(path, request);
        saved.EnsureSuccessStatusCode();
        var assigned = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(featureId, assigned.GetProperty("featureConfigurationId").GetGuid());
        Assert.Equal(name, assigned.GetProperty("featureConfigurationName").GetString());
        Assert.Equal("LAB01", assigned.GetProperty("setupConfigurationId").GetString());
        Assert.False(assigned.GetProperty("wireFormatVerified").GetBoolean());
        var revision = assigned.GetProperty("revision").GetInt32();
        using (var stale = await client.PutAsJsonAsync(path, request))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var same = await client.PutAsJsonAsync(path, new { featureConfigurationId = featureId,
            setupConfigurationId = "LAB01", revision }))
        {
            same.EnsureSuccessStatusCode();
            Assert.Equal(revision, (await same.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32());
        }
        using (var blocked = await client.DeleteAsync($"{featurePath}?revision={featureRevision}&confirm=true"))
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var renamed = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using (var change = await client.PutAsJsonAsync(featurePath, new { name = renamed, revision = featureRevision }))
        {
            change.EnsureSuccessStatusCode();
            featureRevision = (await change.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32();
        }
        var afterRename = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(renamed, afterRename.GetProperty("featureConfigurationName").GetString());

        using (var family = await client.PutAsJsonAsync($"/api/terminals/{inmate.Id}/family", new { terminalFamilyId = "mtr19-multipay" }))
            family.EnsureSuccessStatusCode();
        var inactive = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.False(inactive.GetProperty("eligibleFamily").GetBoolean());
        Assert.Equal(featureId, inactive.GetProperty("featureConfigurationId").GetGuid());
        Assert.Equal("LAB01", inactive.GetProperty("setupConfigurationId").GetString());

        using (var clear = await client.PutAsJsonAsync(path, new { featureConfigurationId = (Guid?)null,
            setupConfigurationId = (string?)null, revision }))
        {
            clear.EnsureSuccessStatusCode();
            var cleared = await clear.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("featureConfigurationId").ValueKind);
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("setupConfigurationId").ValueKind);
            Assert.True(cleared.GetProperty("revision").GetInt32() > revision);
        }
        using (var deleted = await client.DeleteAsync($"{featurePath}?revision={featureRevision}&confirm=true"))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == inmate.Id));
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == inmate.Id));
            Assert.False(await db.TerminalTableAssignments.AnyAsync(row => row.TerminalId == inmate.Id));
        }
    }
}
