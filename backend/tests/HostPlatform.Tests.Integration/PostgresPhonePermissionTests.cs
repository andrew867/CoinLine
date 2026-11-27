using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresPhonePermissionTests
{
    [PostgresIntegrationFact]
    public async Task Add_only_phone_registration_uses_minimal_choices_and_enforces_group_scope()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var user = new OperatorAccount { Username = "fixture-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Fixture operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["phones"] = 4 }) };
        var groupUser = new OperatorAccount { Username = "fixture-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Fixture group operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["phones"] = 4, ["memberships"] = 4 }) };
        var version = new FirmwareVersion { Label = "Fixture firmware" };
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var otherCustomer = new Customer { Name = "Fixture other customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var group = new TerminalGroup { Customer = customer, Name = "Fixture group" };
        var otherGroup = new TerminalGroup { Customer = otherCustomer, Name = "Fixture other group" };
        var sharedGroups = Enumerable.Range(1, 40).Select(index => new TerminalGroup { Name = $"Fixture shared group {index}" }).ToArray();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(user, groupUser, version, site, group, otherGroup);
            db.AddRange(sharedGroups);
            await db.SaveChangesAsync();
        }

        await SignIn(user);
        foreach (var path in new[] { "/api/terminals", "/api/sites", "/api/terminal-groups", "/api/firmware/versions" })
        {
            using var denied = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var choices = await client.GetFromJsonAsync<JsonElement>("/api/terminals/create-options");
        Assert.False(choices.GetProperty("canAssignGroup").GetBoolean());
        Assert.Empty(choices.GetProperty("groups").EnumerateArray());
        Assert.Contains(choices.GetProperty("sites").EnumerateArray(), row => row.GetProperty("id").GetGuid() == site.Id);
        Assert.Contains(choices.GetProperty("firmwareVersions").EnumerateArray(), row => row.GetProperty("id").GetGuid() == version.Id);

        using (var denied = await Create(site.Id, group.Id))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await Create(site.Id, null, [group.Id, sharedGroups[0].Id]))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var invalid = await Create(Guid.NewGuid(), null))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var created = await Create(site.Id, null))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var denied = await client.GetAsync($"/api/terminals/{id}");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        await SignIn(groupUser);
        choices = await client.GetFromJsonAsync<JsonElement>("/api/terminals/create-options");
        Assert.True(choices.GetProperty("canAssignGroup").GetBoolean());
        Assert.Equal(TerminalGroup.MaxMembershipsPerTerminal, choices.GetProperty("maximumGroups").GetInt32());
        Assert.Contains(choices.GetProperty("groups").EnumerateArray(), row => row.GetProperty("id").GetGuid() == group.Id);
        using (var invalid = await Create(site.Id, otherGroup.Id))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var created = await Create(site.Id, group.Id))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var wire = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        foreach (var invalidGroups in new[] { new[] { group.Id, otherGroup.Id }, new[] { group.Id, Guid.NewGuid() },
            new[] { group.Id, group.Id }, new[] { Guid.Empty }, new[] { group.Id }.Concat(sharedGroups.Select(row => row.Id)).ToArray() })
        {
            using var invalid = await Create(site.Id, null, invalidGroups, wire);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using (var inconsistent = await Create(site.Id, sharedGroups[0].Id, [group.Id, sharedGroups[0].Id], wire))
            Assert.Equal(HttpStatusCode.BadRequest, inconsistent.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.False(await db.Terminals.AnyAsync(phone => phone.TerminalIdHex == wire));
        }
        var expectedGroups = new[] { group.Id }.Concat(sharedGroups.Take(39).Select(row => row.Id)).ToArray();
        using (var created = await Create(site.Id, null, expectedGroups, wire))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var phone = await db.Terminals.AsNoTracking().Include(row => row.AdditionalGroups).SingleAsync(row => row.Id == id);
            Assert.Equal(group.Id, phone.TerminalGroupId);
            Assert.Equal(expectedGroups.Skip(1).Order(), phone.AdditionalGroups.Select(row => row.TerminalGroupId).Order());
            Assert.False(await db.TerminalTableAssignments.AnyAsync(row => row.TerminalId == id));
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == id));
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == id));
            Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.TerminalId == id && row.Action == "create"));
        }

        using (var created = await Create(site.Id, group.Id, [group.Id, sharedGroups[0].Id]))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var competingWire = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var competingGroups = new[] { group.Id, sharedGroups[0].Id };
        var competing = await Task.WhenAll(Create(site.Id, null, competingGroups, competingWire), Create(site.Id, null, competingGroups, competingWire));
        using var accepted = Assert.Single(competing, response => response.StatusCode == HttpStatusCode.Created);
        using var rejected = Assert.Single(competing, response => response.StatusCode == HttpStatusCode.Conflict);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var phone = await db.Terminals.AsNoTracking().Include(row => row.AdditionalGroups).SingleAsync(row => row.TerminalIdHex == competingWire);
            Assert.Equal(group.Id, phone.TerminalGroupId);
            Assert.Equal(sharedGroups[0].Id, Assert.Single(phone.AdditionalGroups).TerminalGroupId);
        }

        Task<HttpResponseMessage> Create(Guid siteId, Guid? groupId, Guid[]? groupIds = null, string? wireId = null) => client.PostAsJsonAsync("/api/terminals", new
        {
            siteId, terminalGroupId = groupId, groupIds, firmwareVersionId = version.Id, terminalFamilyId = "mtr19-multipay",
            terminalIdHex = wireId ?? Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), displayName = "Fixture phone", status = 1
        });

        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password });
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    [PostgresIntegrationFact]
    public async Task Phone_and_assignment_lookups_do_not_require_firmware_or_table_authoring_access()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var user = new OperatorAccount { Username = "fixture-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Fixture operator", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["phones"] = 1, ["table-assignments"] = 3 }) };
        var version = new FirmwareVersion { Label = "Fixture firmware" };
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = version, DisplayName = "Fixture phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var set = new TableSet { Name = "Fixture data", Customer = customer, TargetFirmwareVersion = version };
        var raw = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58).First());
        set.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { Name = "Fixture restrictions", TableNumber = 58 },
            TableRevision = 1, EmbeddedPayload = raw, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw) });
        TableDistributionService.MarkPublished(set, DateTime.UtcNow);
        var codeOnly = new TableSet { Name = "Fixture firmware scheduler", Customer = customer };
        var code = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(94).First());
        codeOnly.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { Name = "Fixture scheduler", TableNumber = 94 },
            TableRevision = 1, EmbeddedPayload = code, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(code) });
        TableDistributionService.MarkPublished(codeOnly, DateTime.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(user, phone, set, codeOnly);
            await db.SaveChangesAsync();
        }
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { user.Username, password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using (var denied = await client.GetAsync("/api/firmware/versions"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.GetAsync("/api/tables/sets"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var firmware = await client.GetFromJsonAsync<JsonElement>("/api/terminals/firmware-options");
        var option = Assert.Single(firmware.EnumerateArray(), row => row.GetProperty("id").GetGuid() == version.Id);
        Assert.Equal(new[] { "buildId", "id", "label" }, option.EnumerateObject().Select(field => field.Name).Order().ToArray());
        var choices = await client.GetFromJsonAsync<JsonElement>($"/api/terminals/{phone.Id}/table-assignment-options");
        var choice = Assert.Single(choices.EnumerateArray(), row => row.GetProperty("id").GetGuid() == set.Id);
        Assert.Equal(new[] { "compatibilityIssue", "id", "name", "status", "targetFirmware", "targetFirmwareVersionId" },
            choice.EnumerateObject().Select(field => field.Name).Order().ToArray());
        Assert.Equal(JsonValueKind.Null, choice.GetProperty("compatibilityIssue").ValueKind);
        Assert.DoesNotContain(choices.EnumerateArray(), row => row.GetProperty("id").GetGuid() == codeOnly.Id);

        var path = $"/api/terminals/{phone.Id}/table-assignment";
        using (var assigned = await client.PostAsJsonAsync(path, new { tableSetId = set.Id }))
            assigned.EnsureSuccessStatusCode();
        var saved = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(set.Name, saved.GetProperty("tableSetName").GetString());
        Assert.Equal(version.Id, saved.GetProperty("targetFirmwareVersionId").GetGuid());
        Assert.Equal(version.Label, saved.GetProperty("targetFirmware").GetString());
    }
}
