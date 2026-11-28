using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Firmware;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using HostPlatform.Transport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresFirmwareConfigurationTransitionTests
{
    [PostgresIntegrationFact]
    public Task Staged_configuration_requires_acknowledged_schedule_and_fresh_target_report_before_atomic_activation() => Workflow(false);

    [PostgresIntegrationFact]
    public Task Approved_automatic_activation_delivers_the_original_full_batch_on_first_target_version_install_request() => Workflow(true);

    [PostgresIntegrationFact]
    public Task Approved_automatic_activation_on_alarm_contact_queues_tables_without_interrupting_alarm_receipt() => Workflow(true, false);

    [PostgresIntegrationFact]
    public Task Restore_retained_configuration_only_after_a_separate_live_original_version_report() => Workflow(false, recover: true);

    [PostgresIntegrationFact]
    public Task Scheduled_job_approval_is_bound_once_to_the_exact_transition_and_primary_artifact() => Workflow(false, bindJob: true);

    [PostgresIntegrationFact]
    public Task Incoming_code_server_delivers_only_the_approved_received_schedule_and_holds_target_until_report() =>
        Workflow(false, bindJob: true, serveJob: true);

    [PostgresIntegrationFact]
    public Task Incoming_code_server_rejects_previously_approved_schedule_after_actual_disabled_record_receipt() =>
        Workflow(false, bindJob: true, serveJob: true, cancelBeforeTransfer: true);

    private static async Task Workflow(bool automatic, bool targetInstallContact = true, bool recover = false, bool bindJob = false,
        bool serveJob = false, bool cancelBeforeTransfer = false)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        var codeProbe = new TcpListener(IPAddress.Loopback, 0); codeProbe.Start();
        var codePort = ((IPEndPoint)codeProbe.LocalEndpoint).Port; codeProbe.Stop(); probe.Stop();
        var clock = new FixtureClock();
        var storage = Directory.CreateTempSubdirectory("coinline-code-binding-fixture-");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString())
            .UseSetting("Firmware:AllowLiveFlashing", bindJob ? "true" : "false").UseSetting("DlTransport:LiveDlaEnabled", bindJob ? "true" : "false")
            .UseSetting("IncomingCodeServer:Enabled", serveJob ? "true" : "false").UseSetting("IncomingCodeServer:Port", codePort.ToString())
            .UseSetting("IncomingCodeServer:RequestTimeoutSeconds", "15").UseSetting("IncomingCodeServer:ResponseTimeoutSeconds", "5")
            .UseSetting("IncomingCodeServer:SessionTimeoutSeconds", "60").UseSetting("IncomingCodeServer:AckTimeoutMs", "3000")
            .UseSetting("Platform:PayloadStorage:RootPath", storage.FullName).ConfigureServices(services => services.AddSingleton<TimeProvider>(clock)));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var customer = new Customer { Name = "Transition fixture", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture location", Code = Guid.NewGuid().ToString("N")[..8] };
        var earlier = new FirmwareVersion { Label = "Fixture earlier", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var later = new FirmwareVersion { Label = "Fixture later", TableProfileId = FirmwareTableProfiles.LaterMultiPay180 };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, TerminalFamilyId = "mtr20-multipay", DisplayName = "Fixture phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var wire = Convert.FromHexString(phone.TerminalIdHex);
        var source = new TableSet { Name = "Installed source", Customer = customer, TargetFirmwareVersion = earlier };
        var target = new TableSet { Name = "Prepared target", Customer = customer, TargetFirmwareVersion = later };
        var targetTypes = new[] { 21, 26, 31, 50, 62, 73, 85, 86, 92, 93, 94, 134, 136 };
        var definitions = targetTypes.Concat(new[] { 22, 27, 28, 101 }).ToDictionary(number => number,
            number => new TableDefinition { Name = $"Fixture MT{number}", TableNumber = number });
        var earlierAccess = new byte[18]; earlierAccess[0] = 21; wire.CopyTo(earlierAccess, 1);
        Add(source, 21, earlierAccess);
        if (recover)
        {
            var sourceProfile = FirmwareTableProfiles.Find(earlier.TableProfileId)!;
            foreach (var number in new[] { 22, 26, 27, 28, 31, 50, 62, 73, 92, 93, 101 })
            {
                var length = sourceProfile.VerifiedLengths[number];
                var template = NativeTableLayouts.DraftTemplates(number).SingleOrDefault(row => row.Length == length && row.Profile == sourceProfile.LayoutFor(number));
                var bytes = template == null ? new byte[length] : NativeTableLayouts.CreateDraftTemplate(template);
                bytes[0] = (byte)number; Add(source, number, bytes);
            }
        }
        var profile = FirmwareTableProfiles.Find(later.TableProfileId)!;
        var targetRecords = targetTypes.Where(number => number != 94).ToDictionary(number => number, number =>
        {
            var length = profile.VerifiedLengths[number];
            var template = NativeTableLayouts.DraftTemplates(number).SingleOrDefault(row => row.Length == length && row.Profile == profile.LayoutFor(number));
            var bytes = template == null ? new byte[length] : NativeTableLayouts.CreateDraftTemplate(template);
            bytes[0] = (byte)number; if (number == 21) wire.CopyTo(bytes, 1);
            return bytes;
        });
        foreach (var (number, raw) in targetRecords) Add(target, number, raw);
        var codeSet = new TableSet { Name = "Enabled code schedule fixture", Customer = customer };
        var codeTemplate = Assert.Single(NativeTableLayouts.DraftTemplates(94));
        var codeFields = new Dictionary<string, string>
        {
            ["enabled"] = "true", ["startAt"] = DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["server"] = "12345", ["targetRom"] = "LABNEW", ["targetApp"] = "B001", ["sector-0"] = "true"
        };
        if (bindJob)
        {
            byte[] generatedAuthentication = RandomNumberGenerator.GetBytes(6);
            generatedAuthentication[0] = 0x5a;
            codeFields["serverAuthentication"] = Convert.ToHexString(generatedAuthentication);
            CryptographicOperations.ZeroMemory(generatedAuthentication);
        }
        var code = codeTemplate.Apply(NativeTableLayouts.CreateDraftTemplate(codeTemplate), codeFields);
        Add(codeSet, 94, code);
        foreach (var set in new[] { source, target, codeSet }) TableDistributionService.MarkPublished(set, DateTime.UtcNow);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source, Customer = customer, Site = site };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, source, target, codeSet, assignment); await db.SaveChangesAsync();
        }

        await Report("LABOLD", "A001");
        using var scheduled = await client.PostAsJsonAsync($"/api/tables/sets/{codeSet.Id}/schedules", new
        { targetType = "terminal", targetId = phone.Id, terminalIds = new[] { phone.Id } });
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var schedules = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{codeSet.Id}/schedules");
        var scheduleId = Assert.Single(schedules.EnumerateArray()).GetProperty("id").GetGuid();
        const string endpoint = "/api/firmware/configuration-transitions";
        var request = new { terminalId = phone.Id, targetSetId = target.Id, codeScheduleId = scheduleId, expectedControllerSoftware = "A002", autoActivateOnContact = automatic };
        using var anonymous = factory.CreateClient();
        using (var forbidden = await anonymous.PostAsJsonAsync(endpoint + "/preview", request)) Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
        using var previewResponse = await client.PostAsJsonAsync(endpoint + "/preview", request);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LABNEW", preview.GetProperty("plan").GetProperty("targetControllerRom").GetString());
        Assert.Equal("A002", preview.GetProperty("plan").GetProperty("targetControllerSoftware").GetString());
        Assert.Equal(automatic, preview.GetProperty("plan").GetProperty("autoActivateOnContact").GetBoolean());
        using (var rejected = await client.PostAsJsonAsync(endpoint, new { request.terminalId, request.targetSetId, request.codeScheduleId, request.expectedControllerSoftware, request.autoActivateOnContact, reviewToken = "stale" }))
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using (var changedMode = await client.PostAsJsonAsync(endpoint, new { request.terminalId, request.targetSetId, request.codeScheduleId, request.expectedControllerSoftware, autoActivateOnContact = !automatic, reviewToken = preview.GetProperty("reviewToken").GetString() }))
            Assert.Equal(HttpStatusCode.Conflict, changedMode.StatusCode);
        using var stageResponse = await client.PostAsJsonAsync(endpoint, new { request.terminalId, request.targetSetId, request.codeScheduleId, request.expectedControllerSoftware,
            request.autoActivateOnContact, reviewToken = preview.GetProperty("reviewToken").GetString() });
        Assert.Equal(HttpStatusCode.Created, stageResponse.StatusCode);
        var plan = await stageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var planId = plan.GetProperty("id").GetGuid(); var revision = plan.GetProperty("revision").GetInt32();
        await AssertSourceUnchanged();
        await RejectActivation();

        if (bindJob)
        {
            byte[] image = Enumerable.Range(0, 256 * CodeDownloadWire.CodeBlockBytes).Select(index => (byte)(index % 251)).ToArray();
            string digest = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
            string notes = "Independent fixture backup and recovery reviewed.";
            using var packageResponse = await client.PostAsJsonAsync("/api/firmware/packages", new
            { name = "Generated target", versionLabel = "A002", checksumHex = digest, artifactSizeBytes = image.Length, targetFirmwareVersionId = later.Id });
            packageResponse.EnsureSuccessStatusCode();
            var packageId = (await packageResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            string packagePath = $"/api/firmware/packages/{packageId}";
            using var uploadBody = new ByteArrayContent(image);
            uploadBody.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var upload = await client.PostAsync(packagePath + "/artifacts/upload", uploadBody);
            upload.EnsureSuccessStatusCode();
            var artifactId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var job1 = await Job();
            using (var missingRule = await Approve(job1)) Assert.Equal(HttpStatusCode.BadRequest, missingRule.StatusCode);
            using (var rule = await client.PostAsJsonAsync(packagePath + "/compatibility-rules", new { requiredTerminalFirmwareVersionId = earlier.Id }))
                rule.EnsureSuccessStatusCode();
            var candidates = await client.GetFromJsonAsync<JsonElement>(job1 + "/code-transitions");
            var option = Assert.Single(candidates.EnumerateArray()); Assert.Equal(planId, option.GetProperty("id").GetGuid());
            Assert.Equal(scheduleId, option.GetProperty("codeScheduleId").GetGuid());
            using (var wrongPlan = await client.PostAsJsonAsync(job1 + "/approve", new { rollbackNotes = notes, codeTransitionId = Guid.NewGuid() }))
                Assert.Equal(HttpStatusCode.BadRequest, wrongPlan.StatusCode);
            using (var forbidden = await anonymous.PostAsJsonAsync(job1 + "/approve", new { rollbackNotes = notes, codeTransitionId = planId }))
                Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
            using var ordinary = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "fixture-code-binding-" + Guid.NewGuid().ToString("N"), "Operator");
            using (var forbidden = await ordinary.PostAsJsonAsync(job1 + "/approve", new { rollbackNotes = notes, codeTransitionId = planId }))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var registered = await db.FirmwarePackages.SingleAsync(row => row.Id == packageId);
                registered.TargetFirmwareVersionId = earlier.Id; await db.SaveChangesAsync();
            }
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>(job1 + "/code-transitions")).EnumerateArray());
            using (var wrongTarget = await Approve(job1)) Assert.Equal(HttpStatusCode.BadRequest, wrongTarget.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var registered = await db.FirmwarePackages.SingleAsync(row => row.Id == packageId);
                registered.TargetFirmwareVersionId = later.Id; await db.SaveChangesAsync();
            }
            // A changed registered file must not consume the approval.
            string file = Path.Combine(storage.FullName, "firmware", digest + ".bin"); await File.WriteAllBytesAsync(file, new byte[image.Length]);
            using (var changed = await Approve(job1)) Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
            await File.WriteAllBytesAsync(file, image);
            var job2 = await Job();
            var attempts = await Task.WhenAll(Approve(job1), Approve(job2));
            var winnerIndex = Assert.Single(Enumerable.Range(0, attempts.Length), index => attempts[index].IsSuccessStatusCode);
            Assert.Contains(attempts[1 - winnerIndex].StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
            foreach (var response in attempts) response.Dispose();
            var winner = winnerIndex == 0 ? job1 : job2; var loser = winnerIndex == 0 ? job2 : job1;
            var detail = await client.GetFromJsonAsync<JsonElement>(winner);
            Assert.Equal(planId, detail.GetProperty("codeTransitionId").GetGuid());
            Assert.Equal(scheduleId, detail.GetProperty("codeScheduleId").GetGuid());
            Assert.False(detail.GetProperty("liveExecutionConfigured").GetBoolean());
            Assert.Equal(serveJob, detail.GetProperty("incomingCodeServerListening").GetBoolean());
            using (var outbound = await client.PostAsync(winner + "/execute-live", null))
            {
                Assert.Equal(HttpStatusCode.Conflict, outbound.StatusCode);
                Assert.Contains("terminal-initiated", await outbound.Content.ReadAsStringAsync());
            }
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var bound = await db.FirmwareUpdateJobs.SingleAsync(row => row.CodeScheduleId == scheduleId);
                Assert.Equal(artifactId, bound.FirmwareArtifactId); Assert.Equal(digest, bound.ApprovedArtifactSha256Hex);
                Assert.Equal((long)image.Length, bound.ApprovedArtifactSizeBytes);
                Assert.NotNull(bound.ApprovedAtUtc);
            }
            byte[] incoming = [];
            if (serveJob)
            {
                incoming = new byte[69]; incoming[1] = 1; wire.CopyTo(incoming, 2);
                Encoding.ASCII.GetBytes("LABOLD ").CopyTo(incoming, 7);
                Encoding.ASCII.GetBytes("B001").CopyTo(incoming, 14);
                code.AsSpan(19, 11).CopyTo(incoming.AsSpan(18)); code.AsSpan(68, 6).CopyTo(incoming.AsSpan(29));
                code.AsSpan(36, 32).CopyTo(incoming.AsSpan(37));
                await RejectIncoming(incoming);
            }
            await ExchangeDownload([8], [code]);
            await RejectActivation();
            using (var rejected = await Approve(loser)) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            if (serveJob)
            {
                if (cancelBeforeTransfer)
                {
                    clock.Advance(TimeSpan.FromHours(3));
                    using var clear = await client.PostAsync($"/api/tables/sets/{codeSet.Id}/schedules/{scheduleId}/clear-firmware", null);
                    Assert.True(clear.IsSuccessStatusCode, await clear.Content.ReadAsStringAsync());
                    // Pending replacement must deny the old callback, and a completed
                    // disabled record must continue to deny it after management closes.
                    await RejectIncoming(incoming);
                    await ExchangeDownload([8], [CodeDownloadWire.Disable(code)]);
                    await RejectIncoming(incoming);
                    var retained = await client.GetFromJsonAsync<JsonElement>(winner);
                    Assert.False(retained.GetProperty("steps").EnumerateArray().Any(step => step.GetProperty("name").GetString()!.StartsWith("live_")));
                    Assert.NotEqual(JsonValueKind.Null, retained.GetProperty("approvedAtUtc").ValueKind);
                    await AssertSourceUnchanged();
                    CryptographicOperations.ZeroMemory(incoming);
                    File.Delete(file); Directory.Delete(Path.GetDirectoryName(file)!); storage.Delete();
                    return;
                }
                // The exact receipt alone is not sufficient before the scheduled start.
                await RejectIncoming(incoming);
                clock.Advance(TimeSpan.FromHours(3));
                foreach (var changedOffset in new[] { 2, 7, 18, 25, 29, 37 })
                {
                    var mismatch = incoming.ToArray(); mismatch[changedOffset] ^= 0x02;
                    await RejectIncoming(mismatch);
                }
                var resume = incoming.ToArray(); resume[1] = 2;
                await File.WriteAllBytesAsync(file, new byte[image.Length]);
                await RejectIncoming(incoming); await File.WriteAllBytesAsync(file, image);
                var delivery = await Task.WhenAll(ReceiveIncoming(incoming), ReceiveIncoming(resume));
                Assert.Single(delivery, received => received);
                JsonElement outcome = default;
                for (var retry = 0; retry < 50; retry++)
                {
                    outcome = await client.GetFromJsonAsync<JsonElement>(winner);
                    if (outcome.GetProperty("steps").EnumerateArray().Any(step => step.GetProperty("name").GetString() == "live_code_transport")) break;
                    await Task.Delay(100);
                }
                var steps = outcome.GetProperty("steps").EnumerateArray().ToArray();
                Assert.True(Assert.Single(steps, step => step.GetProperty("name").GetString() == "live_code_claim").GetProperty("succeeded").GetBoolean());
                Assert.True(Assert.Single(steps, step => step.GetProperty("name").GetString() == "live_code_transport").GetProperty("succeeded").GetBoolean());
                Assert.True(Assert.Single(steps, step => step.GetProperty("name").GetString() == "live_code_application_response").GetProperty("succeeded").GetBoolean());
                using var state = JsonDocument.Parse(outcome.GetProperty("safetyStateJson").GetString()!);
                Assert.False(state.RootElement.GetProperty("targetVersionVerified").GetBoolean());
                Assert.False(state.RootElement.GetProperty("serviceVerified").GetBoolean());
                await RejectIncoming(incoming); await RejectIncoming(resume);
                await RejectActivation();
                await AssertSourceUnchanged();
                // A separate matched target report is needed even after a completed code transport.
                await Report("LABNEW", "A002");
                using var activation = await client.PostAsJsonAsync($"{endpoint}/{planId}/activate", new { confirm = true, revision });
                Assert.True(activation.IsSuccessStatusCode, await activation.Content.ReadAsStringAsync());
                var incomingActivated = await activation.Content.ReadFromJsonAsync<JsonElement>();
                var targetBatch = incomingActivated.GetProperty("targetBatchId").GetGuid();
                var expected = TableDistributionService.OrderVersionsForDownload(target.Versions.ToArray())
                    .Select(TableDistributionService.GetEffectivePayload).ToArray();
                await ExchangeDownload([44, 1], expected);
                await using var finalScope = factory.Services.CreateAsyncScope();
                var finalDb = finalScope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var finalBatch = await finalDb.DownloadBatches.Include(row => row.Items).SingleAsync(row => row.Id == targetBatch);
                Assert.Equal(DownloadBatchStatus.Completed, finalBatch.Status);
                Assert.Equal(expected.Length, finalBatch.Items.Count); Assert.All(finalBatch.Items, row => Assert.True(row.Succeeded));
                Assert.Equal(later.Id, (await finalDb.Terminals.FindAsync(phone.Id))!.FirmwareVersionId);
                CryptographicOperations.ZeroMemory(incoming);
                File.Delete(file); Directory.Delete(Path.GetDirectoryName(file)!); storage.Delete();
                return;
            }
            using (var cancel = await client.PostAsJsonAsync(winner + "/cancel", new { confirm = true, reason = "Fixture" })) cancel.EnsureSuccessStatusCode();
            using (var rejected = await Approve(loser)) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            await AssertSourceUnchanged();
            File.Delete(file); Directory.Delete(Path.GetDirectoryName(file)!);
            storage.Delete();
            return;

            async Task<string> Job()
            {
                using var creation = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/firmware-jobs", new
                { firmwarePackageId = packageId, firmwareArtifactId = artifactId, simulationMode = false });
                creation.EnsureSuccessStatusCode();
                string path = "/api/firmware/jobs/" + (await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
                using var simulation = await client.PostAsync(path + "/simulate", null); simulation.EnsureSuccessStatusCode();
                return path;
            }
            Task<HttpResponseMessage> Approve(string path) => client.PostAsJsonAsync(path + "/approve", new { rollbackNotes = notes, codeTransitionId = planId });

            async Task SendIncoming(IAsyncDuplexTransport peer, byte[] requestBytes, CancellationToken ct)
            {
                var pacing = new ModemPacingProfile { InterBlockDelayMs = 0, RetrySpacingMs = 0, AfterControlByteDelayMs = 0 };
                var sent = await XmodemSender.SendAsync(peer, requestBytes, pacing, 3000, 3, TimeSpan.FromSeconds(15), null, null, null, ct);
                Assert.True(sent.Success, sent.Detail);
            }
            async Task RejectIncoming(byte[] requestBytes)
            {
                using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var peer = new TcpClientDuplexTransport("127.0.0.1", codePort);
                await SendIncoming(peer, requestBytes, ct.Token);
                Assert.Equal(0, await peer.ReadAsync(new byte[1], ct.Token));
            }
            async Task<bool> ReceiveIncoming(byte[] requestBytes)
            {
                using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await using var peer = new TcpClientDuplexTransport("127.0.0.1", codePort);
                await SendIncoming(peer, requestBytes, ct.Token);
                try
                {
                    var received = await DlaXmodemReceiver.ReceiveAsync(peer, 3000, 3, 8192, null, ct.Token);
                    Assert.Equal(5, received.PacketCount);
                    Assert.True(received.Data.AsSpan(10, 4096).SequenceEqual(image.AsSpan(0, 4096)));
                    await SendIncoming(peer, [DlaCodeServerSession.ResponseDownload], ct.Token);
                    return true;
                }
                catch (IOException) { return false; }
                catch (TimeoutException) { return false; }
            }
        }
        storage.Delete();

        // Deliver only the scheduled MT94 over the real loopback NCC listener.
        await ExchangeDownload([8], [code]);
        await RejectActivation();
        await ExchangeDownload([44, 2], []);
        await Report("LABOLD", "A001");
        await ExchangeDownload([44, 2], []);

        // A diagnostic version record is not an observed reboot.
        var latest = await client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions?terminalId={phone.Id}&messageType=60");
        using (var imported = await client.PostAsJsonAsync("/api/dlog/transactions/ingest", new { terminalId = phone.Id,
            nccSessionId = latest[0].GetProperty("nccSessionId").GetGuid(), rawPayloadHex = Convert.ToHexString(Version("LABNEW", "A002")),
            direction = (int)DlogDirection.TerminalToHost, messageType = 60, firstByteIsMessageType = true })) Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        await RejectActivation();
        await Report("LABNEW", "A003");
        await RejectActivation();
        JsonElement activated;
        if (automatic)
        {
            // The first actual target-version request is a mandatory full INSTALL.
            // Its complete MT60 trailer authorizes the opted-in activation and
            // selects the original queued target batch on that same connection.
            if (targetInstallContact)
            {
                var requestBundle = new byte[42]; requestBundle[0] = 44; requestBundle[1] = 2; requestBundle[2] = 10;
                Version("LABNEW", "A002").CopyTo(requestBundle, 13); requestBundle[^1] = 13;
                var expected = TableDistributionService.OrderVersionsForDownload(target.Versions.ToArray())
                    .Select(TableDistributionService.GetEffectivePayload).ToArray();
                await ExchangeDownload(requestBundle, expected);
            }
            else
            {
                // Report verifies the exact alarm receipt and EOD, with no table transfer.
                await Report("LABNEW", "A002");
            }
            activated = Assert.Single((await client.GetFromJsonAsync<JsonElement>($"{endpoint}?terminalId={phone.Id}")).EnumerateArray());
        }
        else
        {
            await Report("LABNEW", "A002");
            // A target-version phone cannot receive the old source assignment while the plan waits.
            await ExchangeDownload([44, 2], []);
            var ready = Assert.Single((await client.GetFromJsonAsync<JsonElement>($"{endpoint}?terminalId={phone.Id}")).EnumerateArray());
            Assert.True(ready.GetProperty("state").GetString() == "ready", ready.GetProperty("blocker").GetString());
            using var activatedResponse = await client.PostAsJsonAsync($"{endpoint}/{planId}/activate", new { confirm = true, revision });
            Assert.True(activatedResponse.IsSuccessStatusCode, await activatedResponse.Content.ReadAsStringAsync());
            activated = await activatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        }
        Assert.Equal("activated", activated.GetProperty("state").GetString());
        Assert.Equal(automatic, activated.GetProperty("activatedAutomatically").GetBoolean());
        var batchId = activated.GetProperty("targetBatchId").GetGuid();
        byte[][] targetOrder;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(later.Id, (await db.Terminals.FindAsync(phone.Id))!.FirmwareVersionId);
            var saved = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id);
            Assert.Equal(target.Id, saved.TableSetId); Assert.Equal(source.Id, saved.PreviousTableSetId);
            var batch = await db.DownloadBatches.Include(row => row.Items).ThenInclude(row => row.TableVersion).ThenInclude(row => row!.TableDefinition).SingleAsync(row => row.Id == batchId);
            Assert.Equal(automatic && targetInstallContact ? DownloadBatchStatus.Completed : DownloadBatchStatus.Pending, batch.Status); Assert.Equal(target.Id, batch.TableSetId);
            Assert.Equal(targetRecords.Keys.Order(), batch.Items.Select(row => row.TableVersion!.TableDefinition!.TableNumber).Order());
            targetOrder = batch.Items.OrderBy(row => row.StepIndex).Select(row => targetRecords[row.TableVersion!.TableDefinition!.TableNumber]).ToArray();
        }
        using (var repeated = await client.PostAsJsonAsync($"{endpoint}/{planId}/activate", new { confirm = true, revision })) Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        if (!automatic || !targetInstallContact) await ExchangeDownload([44, 1], targetOrder);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var batch = await db.DownloadBatches.Include(row => row.Items).SingleAsync(row => row.Id == batchId);
            Assert.Equal(DownloadBatchStatus.Completed, batch.Status); Assert.Equal(targetOrder.Length, batch.Items.Count);
            Assert.All(batch.Items, row => Assert.True(row.Succeeded));
            Assert.Equal(2, await db.DownloadBatches.CountAsync(row => row.TerminalId == phone.Id));
        }
        if (recover)
        {
            var recoveryRevision = activated.GetProperty("revision").GetInt32();
            var recoveryPath = $"{endpoint}/{planId}/recover";
            var confirm = new { confirm = true, revision = recoveryRevision };
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(recoveryPath, confirm)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(recoveryPath, confirm)).StatusCode);
            await Report("LABNEW", "A003");
            await ExchangeDownload([44, 2], []);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(recoveryPath, confirm)).StatusCode);
            await Report("LABOLD", "A001");
            await ExchangeDownload([44, 2], []); // Do not send target tables to a reported earlier controller.
            var available = Assert.Single((await client.GetFromJsonAsync<JsonElement>($"{endpoint}?terminalId={phone.Id}")).EnumerateArray());
            Assert.True(available.GetProperty("canRecover").GetBoolean(), available.GetProperty("recoveryBlocker").ToString());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(recoveryPath, new { confirm = false, revision = recoveryRevision })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(recoveryPath, new { confirm = true, revision = recoveryRevision - 1 })).StatusCode);
            using var result = await client.PostAsJsonAsync(recoveryPath, confirm);
            Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
            var restored = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("recovered", restored.GetProperty("state").GetString());
            var recoveryBatchId = restored.GetProperty("recoveryBatchId").GetGuid();
            Assert.NotEqual(batchId, recoveryBatchId);
            await AssertSourceUnchanged();
            byte[][] sourceOrder;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.Equal(target.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id)).PreviousTableSetId);
                var batch = await db.DownloadBatches.Include(row => row.Items).ThenInclude(row => row.TableVersion).ThenInclude(row => row!.TableDefinition).SingleAsync(row => row.Id == recoveryBatchId);
                Assert.Equal(DownloadBatchStatus.Pending, batch.Status); Assert.Equal(source.Id, batch.TableSetId);
                var sourceVersions = source.Versions.ToDictionary(row => row.TableDefinition!.TableNumber, TableDistributionService.GetEffectivePayload);
                Assert.Equal(sourceVersions.Keys.Order(), batch.Items.Select(row => row.TableVersion!.TableDefinition!.TableNumber).Order());
                sourceOrder = batch.Items.OrderBy(row => row.StepIndex).Select(row => sourceVersions[row.TableVersion!.TableDefinition!.TableNumber]).ToArray();
                Assert.Equal(DownloadBatchStatus.Completed, (await db.DownloadBatches.FindAsync(batchId))!.Status);
                Assert.Equal(1, await db.ScheduledTableDownloads.CountAsync(row => row.TerminalId == phone.Id));
            }
            await ExchangeDownload([44, 2], sourceOrder);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var batch = await db.DownloadBatches.Include(row => row.Items).SingleAsync(row => row.Id == recoveryBatchId);
                Assert.Equal(DownloadBatchStatus.Completed, batch.Status); Assert.All(batch.Items, row => Assert.True(row.Succeeded));
                Assert.Equal(3, await db.DownloadBatches.CountAsync(row => row.TerminalId == phone.Id));
            }
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(recoveryPath, confirm)).StatusCode);
        }

        void Add(TableSet set, int number, byte[] raw) => set.Versions.Add(new TableVersion { TableDefinition = definitions[number],
            TableRevision = 1, SortOrder = set.Versions.Count + 1, EmbeddedPayload = raw, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw) });
        async Task RejectActivation()
        {
            using var rejected = await client.PostAsJsonAsync($"{endpoint}/{planId}/activate", new { confirm = true, revision });
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); await AssertSourceUnchanged();
        }
        async Task AssertSourceUnchanged()
        {
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(earlier.Id, (await db.Terminals.FindAsync(phone.Id))!.FirmwareVersionId);
            Assert.Equal(source.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id)).TableSetId);
        }
        async Task WaitClosed()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                if (!await db.NccSessions.AnyAsync(row => row.TerminalId == phone.Id && row.Status == NccSessionStatus.Active)) return;
                await Task.Delay(50);
            }
            throw new TimeoutException("Fixture NCC connection did not close.");
        }
        async Task Report(string rom, string software)
        {
            using (var tcp = new TcpClient())
            {
                using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await tcp.ConnectAsync(IPAddress.Loopback, port, ct.Token); using var stream = tcp.GetStream();
                var bundle = new byte[48]; bundle[0] = 7; bundle[7] = 22; bundle[8] = 10; Version(rom, software).CopyTo(bundle, 19); bundle[^1] = 13;
                await Send(stream, 0, bundle, ct.Token); Assert.NotEqual(0, (await Read(stream, ct.Token)).Control & NccControl.Ack);
                var reply = await Read(stream, ct.Token); Assert.Equal(new byte[] { 16, 22, 13 }, reply.Data);
                await Send(stream, (byte)(NccControl.Ack | NccControl.Clr | reply.Control), [], ct.Token);
            }
            await WaitClosed();
        }
        async Task ExchangeDownload(byte[] requestData, byte[][] expected)
        {
            using (var tcp = new TcpClient())
            {
                using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await tcp.ConnectAsync(IPAddress.Loopback, port, ct.Token); using var stream = tcp.GetStream();
                await Send(stream, 0, requestData, ct.Token); Assert.NotEqual(0, (await Read(stream, ct.Token)).Control & NccControl.Ack);
                if (expected.Length > 0)
                {
                    var header = await Read(stream, ct.Token); Assert.Equal(new byte[] { 18 }, header.Data);
                    await Send(stream, (byte)(NccControl.Ack | header.Control), [], ct.Token);
                    foreach (var raw in expected)
                    {
                        var actual = new List<byte>();
                        while (actual.Count < raw.Length)
                        {
                            var frame = await Read(stream, ct.Token); actual.AddRange(frame.Data);
                            await Send(stream, (byte)(NccControl.Ack | frame.Control), [], ct.Token);
                        }
                        Assert.True(raw.SequenceEqual(actual), "Downloaded fixture record differs."); await Send(stream, 0, [14, raw[0]], ct.Token);
                        Assert.NotEqual(0, (await Read(stream, ct.Token)).Control & NccControl.Ack);
                    }
                }
                var end = await Read(stream, ct.Token); Assert.Equal(new byte[] { 13 }, end.Data);
                await Send(stream, (byte)(NccControl.Ack | NccControl.Clr | end.Control), [], ct.Token);
            }
            await WaitClosed();
        }
        Task Send(NetworkStream stream, byte control, byte[] data, CancellationToken ct) => stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data }), ct).AsTask();
    }

    private static byte[] Version(string rom, string software)
    {
        var record = new byte[28]; record[0] = 60; Encoding.ASCII.GetBytes(rom.PadRight(7)).CopyTo(record, 1); Encoding.ASCII.GetBytes(software).CopyTo(record, 8); return record;
    }

    private sealed class FixtureClock : TimeProvider
    {
        private long offsetTicks;
        public void Advance(TimeSpan offset) => Interlocked.Exchange(ref offsetTicks, offset.Ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref offsetTicks));
    }
    private static async Task<NccWirePacket> Read(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[3]; await stream.ReadExactlyAsync(header, ct);
        var raw = new byte[NccFrameLayout.GetExpectedWireLength(header[2])]; header.CopyTo(raw, 0); await stream.ReadExactlyAsync(raw.AsMemory(3), ct);
        var result = NccFrameCodec.TryDecode(raw, NccParseMode.Strict); Assert.True(result.Success); return result.Packet!;
    }
}
