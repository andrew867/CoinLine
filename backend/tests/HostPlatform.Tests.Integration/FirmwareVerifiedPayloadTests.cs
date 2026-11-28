using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Firmware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HostPlatform.Tests.Integration;

public sealed class FirmwareVerifiedPayloadTests
{
    [Fact]
    public async Task Live_transport_requires_exact_registered_artifact_and_never_synthesizes_bytes()
    {
        var root = Directory.CreateTempSubdirectory("coinline-firmware-fixture-");
        var blob = Path.Combine(root.FullName, "fixture.bin");
        byte[] expected = [0x91, 0x73, 0xA2, 0x44];
        var digest = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
        var adapter = new RecordingAdapter();
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Testing")
                .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
                .UseSetting("Firmware:AllowLiveFlashing", "true").UseSetting("DlTransport:LiveDlaEnabled", "true")
                .UseSetting("Platform:PayloadStorage:RootPath", root.FullName)
                .ConfigureTestServices(services => { services.RemoveAll<IDlXmodemTransportAdapter>(); services.AddSingleton<IDlXmodemTransportAdapter>(adapter); }));
            using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
            var terminal = (await client.GetFromJsonAsync<JsonElement>("/api/terminals"))[0].GetProperty("id").GetGuid();
            using var packageResult = await client.PostAsJsonAsync("/api/firmware/packages", new
            {
                name = "Verified payload fixture", versionLabel = "A001", checksumHex = digest,
                artifactSizeBytes = expected.Length, metadataJson = "{}"
            });
            Assert.Equal(HttpStatusCode.Created, packageResult.StatusCode);
            var package = (await packageResult.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var artifactResult = await client.PostAsJsonAsync($"/api/firmware/packages/{package}/artifacts", new
            {
                kind = "primary", sha256Hex = digest, sizeBytes = expected.Length, storageRef = "fixture.bin", metadataJson = "{}"
            });
            Assert.Equal(HttpStatusCode.Created, artifactResult.StatusCode);
            var artifact = (await artifactResult.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            await Reject(await ApprovedJob(null), "registered artifact");
            var job = await ApprovedJob(artifact);
            await Reject(job, "could not be read");
            await File.WriteAllBytesAsync(blob, [0x91]);
            await Reject(job, "length");
            await File.WriteAllBytesAsync(blob, [0x91, 0x73, 0xA2, 0x45]);
            await Reject(job, "SHA-256");

            await File.WriteAllBytesAsync(blob, expected);
            using var valid = await client.PostAsync($"/api/firmware/jobs/{job}/execute-live", null);
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            Assert.Equal(expected, Assert.Single(adapter.Payloads));
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/firmware/jobs/{job}");
            Assert.Contains(detail.GetProperty("steps").EnumerateArray(), row => row.GetProperty("name").GetString() == "live_payload_prepare" &&
                row.GetProperty("detail").GetString()!.Contains("SHA-256", StringComparison.Ordinal));
            using var repeated = await client.PostAsync($"/api/firmware/jobs/{job}/execute-live", null);
            Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
            Assert.Single(adapter.Payloads);

            async Task<Guid> ApprovedJob(Guid? firmwareArtifactId)
            {
                using var created = await client.PostAsJsonAsync($"/api/terminals/{terminal}/firmware-jobs", new
                    { firmwarePackageId = package, firmwareArtifactId, simulationMode = false });
                created.EnsureSuccessStatusCode();
                var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
                using var simulated = await client.PostAsync($"/api/firmware/jobs/{id}/simulate", null);
                simulated.EnsureSuccessStatusCode();
                using var approved = await client.PostAsJsonAsync($"/api/firmware/jobs/{id}/approve", new { rollbackNotes = "Fixture rollback notes for simulated transport." });
                approved.EnsureSuccessStatusCode();
                return id;
            }

            async Task Reject(Guid id, string reason)
            {
                using var response = await client.PostAsync($"/api/firmware/jobs/{id}/execute-live", null);
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Contains(reason, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
                Assert.Empty(adapter.Payloads);
                var detail = await client.GetFromJsonAsync<JsonElement>($"/api/firmware/jobs/{id}");
                Assert.DoesNotContain(detail.GetProperty("steps").EnumerateArray(), row => row.GetProperty("name").GetString()!.StartsWith("live_", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (File.Exists(blob)) File.Delete(blob);
            root.Delete();
        }
    }

    private sealed class RecordingAdapter : IDlXmodemTransportAdapter
    {
        public List<byte[]> Payloads { get; } = [];
        public Task<DlaTransportSimulationResult> SimulateTransferAsync(DlaTransportSimulationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new DlaTransportSimulationResult(true, "Synthetic test fixture"));
        public Task<DlaLiveTransferResult> ExecuteLiveTransferAsync(DlaLiveTransferRequest request, DlaTransferObservation? observation,
            CancellationToken cancellationToken = default)
        {
            Payloads.Add(request.Payload.ToArray());
            return Task.FromResult(new DlaLiveTransferResult(true, "In-process recording adapter only", DlXmodemSessionState.Completed));
        }
    }
}
