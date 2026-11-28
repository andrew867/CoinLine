using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Firmware;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresFirmwareLiveClaimTests
{
    [PostgresIntegrationFact]
    public async Task Concurrent_live_requests_claim_once_and_keep_the_attempt_history()
    {
        var root = Directory.CreateTempSubdirectory("coinline-live-claim-fixture-");
        var blob = Path.Combine(root.FullName, "fixture.bin");
        byte[] generated = [0x17, 0x28, 0x39];
        await File.WriteAllBytesAsync(blob, generated);
        var digest = Convert.ToHexString(SHA256.HashData(generated)).ToLowerInvariant();
        var key = Guid.NewGuid().ToString("N");
        var adapter = new GatedAdapter(generated);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
                .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
                .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
                .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
                .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
                .UseSetting("Firmware:AllowLiveFlashing", "true").UseSetting("DlTransport:LiveDlaEnabled", "true")
                .UseSetting("Platform:PayloadStorage:RootPath", root.FullName)
                .ConfigureTestServices(services => { services.RemoveAll<IDlXmodemTransportAdapter>(); services.AddSingleton<IDlXmodemTransportAdapter>(adapter); }));
            using var firstClient = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
            using var secondClient = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
            firstClient.DefaultRequestHeaders.Add("X-API-Key", key);
            secondClient.DefaultRequestHeaders.Add("X-API-Key", key);
            var terminal = new Terminal { DisplayName = "Fixture terminal", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                Site = new Site { Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8],
                    Customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] } } };
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.True(db.Database.IsNpgsql());
                db.Terminals.Add(terminal); await db.SaveChangesAsync();
            }
            using var packageResponse = await firstClient.PostAsJsonAsync("/api/firmware/packages", new
            { name = "Generated claim fixture", versionLabel = "A001", checksumHex = digest, artifactSizeBytes = generated.Length, metadataJson = "{}" });
            packageResponse.EnsureSuccessStatusCode();
            var package = (await packageResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var artifactResponse = await firstClient.PostAsJsonAsync($"/api/firmware/packages/{package}/artifacts", new
            { kind = "primary", sha256Hex = digest, sizeBytes = generated.Length, storageRef = "fixture.bin", metadataJson = "{}" });
            artifactResponse.EnsureSuccessStatusCode();
            var artifact = (await artifactResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var created = await firstClient.PostAsJsonAsync($"/api/terminals/{terminal.Id}/firmware-jobs", new
            { firmwarePackageId = package, firmwareArtifactId = artifact, simulationMode = false });
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var path = $"/api/firmware/jobs/{id}";
            using var simulated = await firstClient.PostAsync(path + "/simulate", null);
            simulated.EnsureSuccessStatusCode();
            using var approved = await firstClient.PostAsJsonAsync(path + "/approve", new { rollbackNotes = "Generated fixture rollback and backup notes." });
            approved.EnsureSuccessStatusCode();

            var first = firstClient.PostAsync(path + "/execute-live", null);
            var second = secondClient.PostAsync(path + "/execute-live", null);
            try
            {
                await adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var early = await Task.WhenAny(first, second).WaitAsync(TimeSpan.FromSeconds(15));
                using var rejected = await early;
                Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
                Assert.Equal(1, adapter.Calls);
                var running = await firstClient.GetFromJsonAsync<JsonElement>(path);
                Assert.Equal((int)FirmwareUpdateJobStatus.Running, running.GetProperty("status").GetInt32());
                using var cancel = await secondClient.PostAsJsonAsync(path + "/cancel", new { confirm = true, reason = "Fixture concurrent cancel" });
                Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
                using var resimulate = await secondClient.PostAsync(path + "/simulate", null);
                Assert.Equal(HttpStatusCode.Conflict, resimulate.StatusCode);
                adapter.Release.TrySetResult();
                using var completed = await (early == first ? second : first);
                Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
                using var repeated = await secondClient.PostAsync(path + "/execute-live", null);
                Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
                Assert.Equal(1, adapter.Calls);
                var final = await firstClient.GetFromJsonAsync<JsonElement>(path);
                Assert.Equal((int)FirmwareUpdateJobStatus.Completed, final.GetProperty("status").GetInt32());
                Assert.Equal(2, final.GetProperty("steps").EnumerateArray().Count(step =>
                    step.GetProperty("name").GetString()!.StartsWith("live_", StringComparison.Ordinal)));
            }
            finally { adapter.Release.TrySetResult(); }
        }
        finally
        {
            File.Delete(blob);
            root.Delete();
        }
    }

    private sealed class GatedAdapter(byte[] expected) : IDlXmodemTransportAdapter
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DlaTransportSimulationResult> SimulateTransferAsync(DlaTransportSimulationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new DlaTransportSimulationResult(true, "Generated fixture"));
        public async Task<DlaLiveTransferResult> ExecuteLiveTransferAsync(DlaLiveTransferRequest request,
            DlaTransferObservation? observation, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            Assert.Equal(expected, request.Payload);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(true, "Generated in-process transport", DlXmodemSessionState.Completed);
        }
    }
}
