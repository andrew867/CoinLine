using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresHostRatePlanSelectionTests
{
    [PostgresIntegrationFact]
    public async Task Customer_default_and_phone_override_choose_the_actual_NCC_quote_without_table_download()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var viewer = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "fixture-host-plan-" + Guid.NewGuid().ToString("N"), "Viewer");
        using var anonymous = factory.CreateClient();
        var customer = new Customer { Code = Guid.NewGuid().ToString("N"), Name = "Rating selection fixture" };
        var foreignCustomer = new Customer { Code = Guid.NewGuid().ToString("N"), Name = "Other rating fixture" };
        var site = new Site { Customer = customer, Code = Guid.NewGuid().ToString("N"), Name = "Fixture site" };
        var first = Phone(); var second = Phone();
        Terminal Phone() => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(first, second, foreignCustomer); await db.SaveChangesAsync();
        }
        var legacy = await Plan("Local default", customer.Id, .05m);
        var alternate = await Plan("Regional fixture", customer.Id, .50m);
        var shared = await Plan("Shared fixture", null, .25m);
        var foreign = await Plan("Foreign fixture", foreignCustomer.Id, .90m);
        var draft = await Plan("Unpublished fixture", customer.Id, 1m, publish: false);
        var customerPath = $"/api/rate-plans/selection/customers/{customer.Id}";
        var firstPath = $"/api/rate-plans/selection/terminals/{first.Id}";
        var secondPath = $"/api/rate-plans/selection/terminals/{second.Id}";
        using (var denied = await anonymous.GetAsync(firstPath)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var initialCustomer = await Get(customerPath); Assert.Equal("legacy", Source(initialCustomer)); Assert.Equal(2, initialCustomer.GetProperty("inheritedPhones").GetInt32());
        var initialFirst = await Get(firstPath);
        Assert.Contains(initialFirst.GetProperty("options").EnumerateArray(), option => option.GetProperty("id").GetGuid() == shared.plan);
        Assert.DoesNotContain(initialFirst.GetProperty("options").EnumerateArray(), option => option.GetProperty("id").GetGuid() is var id && (id == foreign.plan || id == draft.plan));
        using (var denied = await viewer.PutAsJsonAsync(firstPath, Body(initialFirst, shared.plan))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await admin.PutAsJsonAsync(firstPath, Body(initialFirst, foreign.plan))) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using (var denied = await admin.PutAsJsonAsync(firstPath, Body(initialFirst, draft.plan))) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await Quote(first, legacy, 5); await Quote(second, legacy, 5);
        await Set(customerPath, initialCustomer, alternate.plan);
        var chosenCustomer = await Get(customerPath); Assert.Equal("customer", Source(chosenCustomer));
        Assert.Equal("customer", Source(await Get(firstPath)));
        await Quote(first, alternate, 50); await Quote(second, alternate, 50);
        await Set(firstPath, await Get(firstPath), shared.plan);
        var chosenFirst = await Get(firstPath); Assert.Equal("terminal", Source(chosenFirst));
        Assert.Equal(1, (await Get(customerPath)).GetProperty("inheritedPhones").GetInt32());
        Assert.Equal("customer", Source(await Get(secondPath)));
        await Quote(first, shared, 25); await Quote(second, alternate, 50);
        using (var stale = await admin.PutAsJsonAsync(firstPath, Body(initialFirst, legacy.plan))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        // An invalid explicit selection must not fall back to a customer's differently priced plan.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var saved = await db.HostRatePlanSelections.SingleAsync(s => s.TerminalId == first.Id);
            saved.RatePlanId = foreign.plan; saved.Version++; await db.SaveChangesAsync();
        }
        Assert.Equal("terminal-unavailable", Source(await Get(firstPath)));
        await Quote(first, null, null);
        await Set(firstPath, await Get(firstPath), null); await Quote(first, alternate, 50);
        await Set(customerPath, await Get(customerPath), null); await Quote(first, legacy, 5);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Empty(await db.HostRatePlanSelections.Where(s => s.CustomerId == customer.Id || s.TerminalId == first.Id || s.TerminalId == second.Id).ToListAsync());
            Assert.Empty(await db.DownloadBatches.Where(b => b.TerminalId == first.Id || b.TerminalId == second.Id).ToListAsync());
            var calls = await db.CallRecords.AsNoTracking().Where(c => c.TerminalId == first.Id || c.TerminalId == second.Id).OrderBy(c => c.StartedAtUtc).ToListAsync();
            Assert.Equal(new Guid?[] { legacy.version, legacy.version, alternate.version, alternate.version, shared.version,
                alternate.version, null, alternate.version, legacy.version }, calls.Select(c => c.AppliedRatePlanVersionId).ToArray());
        }

        async Task<(Guid plan, Guid version)> Plan(string name, Guid? scopeId, decimal amount, bool publish = true)
        {
            var id = await Create("/api/rate-plans", new { name, mode = RatingMode.RealTimeRated, customerId = scopeId });
            var version = await Create($"/api/rate-plans/{id}/versions", new { });
            await Create($"/api/rate-plans/{id}/versions/{version}/rules", new { priority = 200, pattern = "7095550123", matchKind = 2,
                outcome = 0, pricingMode = 1, initialChargeUsd = amount, ratePerMinuteUsd = 0m });
            if (publish)
            {
                using var response = await admin.PostAsJsonAsync($"/api/rate-plans/{id}/publish", new { ratePlanVersionId = version, confirm = true });
                response.EnsureSuccessStatusCode();
            }
            return (id, version);
        }
        async Task<Guid> Create(string path, object body)
        {
            using var response = await admin.PostAsJsonAsync(path, body); response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        async Task<JsonElement> Get(string path) => await admin.GetFromJsonAsync<JsonElement>(path);
        static string? Source(JsonElement data) => data.GetProperty("effective").GetProperty("source").GetString();
        static object Body(JsonElement current, Guid? id) => new { ratePlanId = id, expectedSelectionId = current.GetProperty("selectionId"),
            expectedRevision = current.GetProperty("revision"), confirm = true };
        async Task Set(string path, JsonElement current, Guid? id)
        {
            using var response = await admin.PutAsJsonAsync(path, Body(current, id)); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        async Task Quote(Terminal phone, (Guid plan, Guid version)? selected, ushort? amount)
        {
            using var tcp = new TcpClient(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            using var stream = tcp.GetStream(); var wireId = Convert.FromHexString(phone.TerminalIdHex);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = wireId,
                Data = DlogRateWire.EncodeRequest("7095550123", false) }), deadline.Token);
            var ack = await ReadFrame(stream, deadline.Token); Assert.NotEqual(0, ack.Control & NccControl.Ack);
            var reply = await ReadFrame(stream, deadline.Token);
            Assert.Equal(DlogRateWire.EncodeResponse(amount is { } cents
                ? DlogRateWire.QuoteFromPricing(cents / 100m, 0, true, 0)
                : DlogRateWire.QuoteFromDecision(false, false, 0)), reply.Data);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = wireId,
                Control = (byte)(NccControl.Ack | NccControl.Clr | reply.Control) }), deadline.Token);
        }
    }
    private static async Task<NccWirePacket> ReadFrame(NetworkStream stream, CancellationToken ct)
    {
        var head = new byte[3]; await stream.ReadExactlyAsync(head, ct);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(head[2])]; head.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), ct);
        var result = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(result.Success); return result.Packet!;
    }
}
