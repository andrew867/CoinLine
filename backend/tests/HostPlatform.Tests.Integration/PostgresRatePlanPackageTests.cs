using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Rating;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresRatePlanPackageTests
{
    [PostgresIntegrationFact]
    public async Task Starter_and_portable_import_create_only_independent_drafts_with_correct_reference_and_quote_semantics()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Enabled", "false").UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var viewer = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "fixture-rating-package-" + Guid.NewGuid().ToString("N"), "Viewer");
        using var anon = factory.CreateClient();
        const string url = "/api/rate-plans/packages";
        using (var rejected = await anon.GetAsync(url + "/regional-starter")) Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var starter = await admin.GetFromJsonAsync<RatePlanPackage>(url + "/regional-starter");
        Assert.NotNull(starter); Assert.Null(starter.ValidationIssue());
        var package = starter with
        {
            Rules = [.. starter.Rules, new PackageRateRule(250, RateRuleMatchKind.Exact, "7095550174", RateRuleOutcome.Free,
                CallPricingMode.LegacyProrated, 0, 0)],
            Tariffs = [new(42, "Fixture catalogue", CallPricingMode.FlatCall, 0.35m, 0m, "Reviewed")],
            DestinationPrefixes = [new("555", 42, "")], TimeBands = [new(127, 200, 300, 42)]
        };
        var body = new { package, name = "Fixture portable regional tariff " + Guid.NewGuid().ToString("N")[..8] };
        var counts = await Snapshot();
        using (var read = await viewer.PostAsJsonAsync(url + "/preview", body)) Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using (var refused = await viewer.PostAsJsonAsync(url + "/import", body)) Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using (var malformed = await admin.PostAsJsonAsync(url + "/import", new { package = package with { FormatVersion = 2 } }))
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(counts, await Snapshot());
        using var created = await admin.PostAsJsonAsync(url + "/import", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = await created.Content.ReadFromJsonAsync<JsonElement>();
        var planId = result.GetProperty("id").GetGuid(); var versionId = result.GetProperty("versionId").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var plan = await db.RatePlans.Include(p => p.Versions).ThenInclude(v => v.Tariffs)
                .Include(p => p.Versions).ThenInclude(v => v.Rules)
                .Include(p => p.Versions).ThenInclude(v => v.TimeBands)
                .Include(p => p.Versions).ThenInclude(v => v.DestinationPrefixes).SingleAsync(p => p.Id == planId);
            Assert.Null(plan.PublishedVersionId); Assert.Null(plan.CustomerId);
            var version = Assert.Single(plan.Versions);
            Assert.Equal(RatePlanVersionStatus.Draft, version.Status);
            Assert.Equal(package.Rules.Length, version.Rules.Count);
            var tariff = Assert.Single(version.Tariffs);
            Assert.Equal(tariff.Id, Assert.Single(version.TimeBands).TariffId);
            Assert.Equal(tariff.Id, Assert.Single(version.DestinationPrefixes).TariffId);
            Assert.Equal(2, version.Rules.Count(RateRulePolicy.OverridesFreeClass));
        }
        var export = await viewer.GetFromJsonAsync<RatePlanPackage>($"{url}/{planId}/{versionId}");
        Assert.NotNull(export); Assert.Equal(package.Rules.Length, export.Rules.Length);
        Assert.Equal(export.Tariffs.Single().Key, export.TimeBands.Single().TariffKey);
        Assert.Equal(export.Tariffs.Single().Key, export.DestinationPrefixes.Single().TariffKey);
        var overlay = await admin.GetFromJsonAsync<RatePlanPackage>($"{url}/regional-starter?sourcePlanId={planId}&sourceVersionId={versionId}");
        Assert.NotNull(overlay);
        Assert.Equal(starter.Rules.Length + 1, overlay.Rules.Length);
        Assert.Equal("7095550174", Assert.Single(overlay.Rules, rule => rule.Priority == 250).Pattern);
        using var copied = await admin.PostAsJsonAsync(url + "/import", new { package = export, name = body.name + " copied" });
        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        var copyId = (await copied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.NotEqual(planId, copyId);
        Assert.Equal((counts.Terminals, counts.Assignments, counts.Downloads), ((await Snapshot()).Terminals, (await Snapshot()).Assignments, (await Snapshot()).Downloads));
        // A separate publication is required before the actual host quote route uses the plan.
        using (var publish = await admin.PostAsJsonAsync($"/api/rate-plans/{planId}/publish", new { ratePlanVersionId = versionId, confirm = true }))
            publish.EnsureSuccessStatusCode();
        // Use the starter without a broad time-band fallback to evaluate exact dialing form.
        using var plain = await admin.PostAsJsonAsync(url + "/import", new { package = starter, name = body.name + " pricing" });
        plain.EnsureSuccessStatusCode();
        var plainResult = await plain.Content.ReadFromJsonAsync<JsonElement>();
        var plainId = plainResult.GetProperty("id").GetGuid();
        using (var publish = await admin.PostAsJsonAsync($"/api/rate-plans/{plainId}/publish",
            new { ratePlanVersionId = plainResult.GetProperty("versionId").GetGuid(), confirm = true })) publish.EnsureSuccessStatusCode();
        await Price("7095550123", 100, .25m, true); await Price("17095550123", 2, .75m, true);
        await Price("14165550123", 2, 1.25m, true); await Price("12025550123", 2, 2.00m, true);
        await Price("2025550123", 2, 0m, false); await Price("5550123", 2, 0m, false);
        await Price("411", 100, 1m, true); await Price("711", 2.01m, .75m, true);
        await Price("18005550123", 100, 0m, true); await Price("911", 100, 0m, true);

        async Task Price(string digits, decimal duration, decimal amount, bool allowed)
        {
            using var quote = await admin.PostAsJsonAsync("/api/rating/quote", new { dialedDigits = digits, mode = 1, ratePlanId = plainId, assumedDurationMinutes = duration });
            quote.EnsureSuccessStatusCode();
            var data = await quote.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(amount, data.GetProperty("amountUsd").GetDecimal());
            Assert.Equal(allowed, data.GetProperty("allowed").GetBoolean());
        }
        async Task<(int Plans, int Terminals, int Assignments, int Downloads)> Snapshot()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            return (await db.RatePlans.CountAsync(), await db.Terminals.CountAsync(), await db.TerminalTableAssignments.CountAsync(), await db.DownloadBatches.CountAsync());
        }
    }
}
