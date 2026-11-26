using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HostPlatform.Tests.Integration;

/// <summary>Dual-gated opaque craft write onto a bound NCC socket.</summary>
public sealed class NccCraftLiveFixture : IAsyncLifetime
{
    private const string LiveCraftFlag = "COINLINE_CRAFT_LIVE_ENABLED";
    private string? _previousLiveCraftFlag;

    public NccCraftLiveFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        _previousLiveCraftFlag = Environment.GetEnvironmentVariable(LiveCraftFlag);
        Environment.SetEnvironmentVariable(LiveCraftFlag, "1");
        Factory = new NccCraftLiveFactory();
        Port = Factory.Port;
        Client = TestOperatorAuthentication.CreateAuthenticatedClient(Factory, "craft-live@local", "Admin");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, Port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }

        throw new TimeoutException("NCC listener did not accept a connection.");
    }

    public async Task DisposeAsync()
    {
        try
        {
            Client?.Dispose();
            if (Factory is not null)
                await Factory.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(LiveCraftFlag, _previousLiveCraftFlag);
        }
    }
}

public sealed class NccCraftLiveFactory : WebApplicationFactory<Program>
{
    public int Port { get; }

    public NccCraftLiveFactory()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint!).Port;
        probe.Stop();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey);
        builder.UseSetting("Ncc:Listen", "1");
        builder.UseSetting("Ncc:Port", Port.ToString());
        builder.UseSetting("Craft:AllowLiveCraft", "true");
    }
}

[CollectionDefinition("NccCraftLive", DisableParallelization = true)]
public sealed class NccCraftLiveCollection;

[Collection("NccCraftLive")]
public sealed class NccCraftLiveTests(NccCraftLiveFixture fx) : IClassFixture<NccCraftLiveFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] KnownId = Convert.FromHexString("0102030405");

    [Fact]
    public async Task Live_craft_frames_opaque_payload_onto_active_link()
    {
        var policy = await fx.Client.GetFromJsonAsync<JsonElement>("/api/craft/execution-policy", Json);
        Assert.True(policy!.GetProperty("allowLiveCraft").GetBoolean());
        Assert.True(policy.GetProperty("liveEnabled").GetBoolean());

        var term = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json))![0];
        var termId = term.GetProperty("id").GetGuid();

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
        var stream = tcp.GetStream();
        // Alarm-only body binds the session without starting an install batch.
        var hello = Message(KnownId, [0x07, 0x1A, 0x0B, 0x17, 0x0C, 0x00, 0x00, 0x2A]);
        await stream.WriteAsync(hello);
        _ = await ReadFrameAsync(stream, TimeSpan.FromSeconds(5));

        var session = await fx.Client.PostAsJsonAsync("/api/craft/sessions",
            new { terminalId = termId, technicianId = "tech-live", operatorId = "op-live", fieldNotes = "live write" });
        Assert.Equal(HttpStatusCode.Created, session.StatusCode);
        var sid = JsonSerializer.Deserialize<JsonElement>(await session.Content.ReadAsStringAsync(), Json).GetProperty("id")
            .GetGuid();

        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/craft/sessions/{sid}", Json);
        Assert.Equal(JsonValueKind.Object, detail!.GetProperty("liveLink").ValueKind);

        var payload = new byte[] { 0x7E, 0x01, 0x02 };
        var cmd = await fx.Client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "ping",
            requestHex = Convert.ToHexString(payload),
            commandTypeCode = "ping",
            simulationExecution = false
        });
        Assert.Equal(HttpStatusCode.OK, cmd.StatusCode);
        var cmdBody = JsonSerializer.Deserialize<JsonElement>(await cmd.Content.ReadAsStringAsync(), Json);
        Assert.Equal(2, cmdBody.GetProperty("status").GetInt32()); // Succeeded

        var framed = await ReadFrameAsync(stream, TimeSpan.FromSeconds(5));
        var parsed = NccFrameCodec.TryDecode(framed, NccParseMode.DiagnosticCapture);
        Assert.True(parsed.Success);
        Assert.NotNull(parsed.Packet);
        Assert.Equal(payload, parsed.Packet!.Data.ToArray());
        Assert.Equal(KnownId, parsed.Packet.TerminalId);
    }

    [Fact]
    public async Task Live_craft_without_active_link_conflicts()
    {
        var term = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json))![0].GetProperty("id")
            .GetGuid();
        var session = await fx.Client.PostAsJsonAsync("/api/craft/sessions",
            new { terminalId = term, technicianId = "tech-x", operatorId = "op-x", fieldNotes = "" });
        var sid = JsonSerializer.Deserialize<JsonElement>(await session.Content.ReadAsStringAsync(), Json).GetProperty("id")
            .GetGuid();

        var cmd = await fx.Client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "ping",
            requestHex = "01",
            simulationExecution = false
        });
        Assert.Equal(HttpStatusCode.Conflict, cmd.StatusCode);
        var body = await cmd.Content.ReadAsStringAsync();
        Assert.Contains("No active NCC link", body, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Message(byte[] terminalId, byte[] data)
    {
        return NccFrameCodec.Encode(new NccWirePacket
        {
            Control = 0,
            TerminalId = terminalId,
            Data = data
        });
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, TimeSpan timeout)
    {
        var buf = new byte[512];
        var got = new List<byte>();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (stream.DataAvailable)
            {
                var n = await stream.ReadAsync(buf);
                got.AddRange(buf.AsSpan(0, n).ToArray());
                var arr = got.ToArray();
                if (arr.Length >= 6 && arr[0] == NccConstants.FrameStart && arr[^1] == NccConstants.FrameEnd)
                    return arr;
            }
            else
                await Task.Delay(20);
        }

        throw new TimeoutException($"No NCC frame. bytes={Convert.ToHexString(got.ToArray())}");
    }
}
