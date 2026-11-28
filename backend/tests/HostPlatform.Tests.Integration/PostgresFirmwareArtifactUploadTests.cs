using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
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
public sealed class PostgresFirmwareArtifactUploadTests
{
    [PostgresIntegrationFact]
    public async Task Upload_checks_registered_bytes_reuses_immutable_blob_and_prepares_exact_job_artifact()
    {
        var root = Directory.CreateTempSubdirectory("coinline-artifact-upload-fixture-");
        byte[] generated = [0x12, 0x34, 0x56, 0x78];
        var digest = Convert.ToHexString(SHA256.HashData(generated)).ToLowerInvariant();
        var folder = Path.Combine(root.FullName, "firmware"); var stored = Path.Combine(folder, digest + ".bin");
        var adapter = new RecordingAdapter();
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
                .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
                .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
                .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
                .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false")
                .UseSetting("Firmware:AllowLiveFlashing", "true").UseSetting("DlTransport:LiveDlaEnabled", "true")
                .UseSetting("Platform:PayloadStorage:RootPath", root.FullName)
                .ConfigureTestServices(services => { services.RemoveAll<IDlXmodemTransportAdapter>(); services.AddSingleton<IDlXmodemTransportAdapter>(adapter); }));
            using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
            using var nonAdmin = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "fixture-upload-" + Guid.NewGuid().ToString("N"), "Operator");
            using var anonymous = factory.CreateClient();
            var phone = new Terminal { DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                Site = new Site { Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8], Customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] } } };
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql()); db.Terminals.Add(phone); await db.SaveChangesAsync();
            }
            var package = await Package(); var path = $"/api/firmware/packages/{package}";
            using (var response = await Upload(anonymous, path, generated)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await Upload(nonAdmin, path, generated)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await Upload(admin, path, [0x12])) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await Upload(admin, path, [0x12, 0x34, 0x56, 0x79]))
            { Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("SHA-256", await response.Content.ReadAsStringAsync()); }
            Assert.False(File.Exists(stored)); Assert.Empty(Directory.EnumerateFiles(folder));
            using (var badType = await admin.PostAsJsonAsync(path + "/artifacts/upload", new { bytes = "fixture" })) Assert.Equal(HttpStatusCode.UnsupportedMediaType, badType.StatusCode);
            var before = await admin.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(JsonValueKind.Null, before.GetProperty("primaryArtifactId").ValueKind); Assert.Empty(before.GetProperty("artifacts").EnumerateArray());

            using var upload = await Upload(admin, path, generated); Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            var result = await upload.Content.ReadFromJsonAsync<JsonElement>(); var artifact = result.GetProperty("id").GetGuid();
            Assert.Equal(digest, result.GetProperty("sha256Hex").GetString()); Assert.True(result.GetProperty("payloadPresent").GetBoolean());
            Assert.Equal(generated, await File.ReadAllBytesAsync(stored));
            using var duplicate = await Upload(admin, path, generated); Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            Assert.Equal(artifact, (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            var detail = await admin.GetFromJsonAsync<JsonElement>(path); Assert.Equal(artifact, detail.GetProperty("primaryArtifactId").GetGuid());
            var row = Assert.Single(detail.GetProperty("artifacts").EnumerateArray()); Assert.True(row.GetProperty("payloadPresent").GetBoolean());
            Assert.Equal($"firmware/{digest}.bin", row.GetProperty("storageRef").GetString());

            var secondPackage = await Package(); using var shared = await Upload(admin, $"/api/firmware/packages/{secondPackage}", generated);
            Assert.Equal(HttpStatusCode.Created, shared.StatusCode); Assert.Single(Directory.EnumerateFiles(folder));
            Assert.NotEqual(artifact, (await shared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

            using var created = await admin.PostAsJsonAsync($"/api/terminals/{phone.Id}/firmware-jobs", new
            { firmwarePackageId = package, firmwareArtifactId = artifact, simulationMode = false });
            created.EnsureSuccessStatusCode(); var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var jobPath = $"/api/firmware/jobs/{jobId}";
            using (var response = await admin.PostAsync(jobPath + "/simulate", null)) response.EnsureSuccessStatusCode();
            using (var response = await admin.PostAsJsonAsync(jobPath + "/approve", new { rollbackNotes = "Fixture backup and rollback review." })) response.EnsureSuccessStatusCode();
            using (var response = await admin.PostAsync(jobPath + "/execute-live", null)) response.EnsureSuccessStatusCode();
            Assert.Equal(generated, Assert.Single(adapter.Payloads));

            byte[] wrong = [0x12, 0x34, 0x56, 0x79]; await File.WriteAllBytesAsync(stored, wrong);
            using (var response = await Upload(admin, path, generated)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(wrong, await File.ReadAllBytesAsync(stored)); Assert.Single(Directory.EnumerateFiles(folder));
            var final = await admin.GetFromJsonAsync<JsonElement>(path); Assert.Equal(artifact, final.GetProperty("primaryArtifactId").GetGuid());
            Assert.Single(final.GetProperty("artifacts").EnumerateArray());

            if (OperatingSystem.IsLinux())
            {
                // An administrator-configured payload root must not follow a redirected subdirectory.
                File.Delete(stored); Directory.Delete(folder);
                var outside = Directory.CreateTempSubdirectory("coinline-artifact-unrelated-fixture-");
                try
                {
                    Directory.CreateSymbolicLink(folder, outside.FullName);
                    using var rejected = await Upload(admin, path, generated);
                    Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
                    Assert.Empty(outside.EnumerateFileSystemInfos());
                    var unavailable = await admin.GetFromJsonAsync<JsonElement>(path);
                    Assert.False(Assert.Single(unavailable.GetProperty("artifacts").EnumerateArray()).GetProperty("payloadPresent").GetBoolean());
                }
                finally { if (Directory.Exists(folder)) Directory.Delete(folder); outside.Delete(); }
            }

            async Task<Guid> Package()
            {
                using var response = await admin.PostAsJsonAsync("/api/firmware/packages", new { name = "Generated fixture", versionLabel = "A001", checksumHex = digest, artifactSizeBytes = generated.Length, metadataJson = "{}" });
                response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            }
        }
        finally { if (File.Exists(stored)) File.Delete(stored); if (Directory.Exists(folder)) Directory.Delete(folder); root.Delete(); }
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string packagePath, byte[] body)
    {
        using var content = new ByteArrayContent(body); content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await client.PostAsync(packagePath + "/artifacts/upload", content);
    }

    private sealed class RecordingAdapter : IDlXmodemTransportAdapter
    {
        public List<byte[]> Payloads { get; } = [];
        public void ValidateLiveConfiguration(DlaTransferObservation? observation) { }
        public Task<DlaTransportSimulationResult> SimulateTransferAsync(DlaTransportSimulationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new DlaTransportSimulationResult(true, "Generated fixture"));
        public Task<DlaLiveTransferResult> ExecuteLiveTransferAsync(DlaLiveTransferRequest request, DlaTransferObservation? observation, CancellationToken cancellationToken = default)
        { Payloads.Add(request.Payload.ToArray()); return Task.FromResult(new DlaLiveTransferResult(true, "Generated in-process transport", DlXmodemSessionState.Completed)); }
    }
}
