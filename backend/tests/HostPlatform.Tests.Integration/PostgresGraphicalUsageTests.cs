using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresGraphicalUsageTests
{
    [PostgresIntegrationFact]
    public async Task Graphical_usage_chunks_with_shared_period_are_listed_and_deduplicated_on_postgres()
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
        var customer = new Customer { Name = "Graphical fixture", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, DisplayName = "Graphical fixture", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.Terminals.Add(phone);
            await db.SaveChangesAsync();
        }

        var first = Report(1, 40);
        var second = Report(24, 60);
        await Task.WhenAll(Store(first), Store(first));
        await Store(second);
        await Store(second);
        var uploads = await client.GetFromJsonAsync<JsonElement>("/api/uploads");
        var rows = uploads.EnumerateArray().Where(row => row.GetProperty("terminalId").ValueKind != JsonValueKind.Null &&
            row.GetProperty("terminalId").GetGuid() == phone.Id).ToArray();
        Assert.Equal(2, rows.Length);
        var screenNumbers = new List<int>();
        foreach (var row in rows)
        {
            Assert.Equal(153, row.GetProperty("statisticsMessageType").GetInt32());
            Assert.Equal("Graphical usage statistics", row.GetProperty("statisticsName").GetString());
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/uploads/{row.GetProperty("id").GetGuid()}");
            var report = Assert.Single(detail.GetProperty("records").EnumerateArray()).GetProperty("terminalStatistics");
            Assert.Equal("2028-02-29T10:11:12", report.GetProperty("periodStart").GetString());
            var screen = Assert.Single(report.GetProperty("graphicalScreens").EnumerateArray());
            screenNumbers.Add(screen.GetProperty("screenNumber").GetInt32());
            Assert.Equal(1234, screen.GetProperty("ownerCode").GetInt32());
        }
        Assert.Equal(new[] { 1, 24 }, screenNumbers.Order().ToArray());

        async Task Store(byte[] raw)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var report = DlogTerminalStatisticsWire.Decode(raw, out var fingerprint);
            await TerminalUploads.StoreAsync(db, phone.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
        }

        static byte[] Report(byte screen, ushort count)
        {
            var result = new byte[245];
            result[0] = 153;
            new byte[] { 128, 2, 29, 10, 11, 12, 128, 3, 1, 10, 11, 12 }.CopyTo(result, 1);
            result[13] = screen;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 1234);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(18), count);
            return result;
        }
    }
}
