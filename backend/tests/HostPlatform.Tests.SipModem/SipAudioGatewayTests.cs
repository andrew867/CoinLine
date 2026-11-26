using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using HostPlatform.SipModem;
using HostPlatform.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostPlatform.Tests.SipModem;

public sealed class SipAudioGatewayTests
{
    [Fact]
    public async Task G711_call_delivers_bell202_bytes_to_the_listener()
    {
        using var ncc = new TcpListener(IPAddress.Loopback, 0);
        ncc.Start();
        var nccPort = ((IPEndPoint)ncc.LocalEndpoint).Port;
        var accepted = ncc.AcceptTcpClientAsync();

        await using var gateway = new SipAudioGateway(new SipModemOptions
        {
            Enabled = true,
            SipPort = 0,
            RtpPortStart = 0,
            NccHost = "127.0.0.1",
            NccPort = nccPort,
            PublicAddress = "127.0.0.1"
        }, NullLogger.Instance);
        gateway.Start();
        AssertLoopbackUdp(gateway.SipPort);

        using var phone = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var phonePort = ((IPEndPoint)phone.Client.LocalEndPoint!).Port;
        var invite = Invite(phonePort, gateway.SipPort, "0", "PCMU/8000");
        await phone.SendAsync(Encoding.ASCII.GetBytes(invite), new IPEndPoint(IPAddress.Loopback, gateway.SipPort));

        var ok = await ReadSipAsync(phone, "SIP/2.0 200");
        Assert.Contains("SIP/2.0 200", ok, StringComparison.Ordinal);
        var rtpPort = RtpPort(ok);
        AssertLoopbackUdp(rtpPort);
        var payload = new byte[] { 0x02, 0x11, 0x22 };
        await SendModemAudioAsync(phone, rtpPort, payload);

        var client = await accepted.WaitAsync(TimeSpan.FromSeconds(5));
        using var _ = client;
        var stream = client.GetStream();
        var got = new List<byte>();
        var deadline = DateTime.UtcNow.AddSeconds(6);
        var buf = new byte[64];
        while (DateTime.UtcNow < deadline)
        {
            if (stream.DataAvailable)
                got.AddRange(buf.AsSpan(0, await stream.ReadAsync(buf)));
            else if (ContainsSequence(got, payload))
                break;
            else
                await Task.Delay(20);
        }

        Assert.True(ContainsSequence(got, payload), $"bytes={Convert.ToHexString(got.ToArray())}");
    }

    [Fact]
    public async Task Lab_probe_answers_a_loopback_g711_call()
    {
        using var ncc = new TcpListener(IPAddress.Loopback, 0);
        ncc.Start();
        var nccPort = ((IPEndPoint)ncc.LocalEndpoint).Port;
        var accepted = ncc.AcceptTcpClientAsync();

        await using var gateway = new SipAudioGateway(new SipModemOptions
        {
            Enabled = true,
            SipPort = 0,
            RtpPortStart = 0,
            NccHost = "127.0.0.1",
            NccPort = nccPort,
            PublicAddress = "127.0.0.1"
        }, NullLogger.Instance);
        gateway.Start();

        var payload = new byte[] { 0x01, 0xA5, 0x5A };
        var result = await SipModemLabProbe.RunAsync(gateway.SipPort, payload, CancellationToken.None);
        Assert.True(result.Ok, result.Detail);
        Assert.Equal(payload.Length, result.BytesSent);

        using var client = await accepted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(client.Connected);
    }

    [Fact]
    public async Task Hangup_all_clears_active_calls()
    {
        using var ncc = new TcpListener(IPAddress.Loopback, 0);
        ncc.Start();
        var nccPort = ((IPEndPoint)ncc.LocalEndpoint).Port;
        _ = ncc.AcceptTcpClientAsync();

        await using var gateway = new SipAudioGateway(new SipModemOptions
        {
            Enabled = true,
            SipPort = 0,
            RtpPortStart = 0,
            NccHost = "127.0.0.1",
            NccPort = nccPort,
            PublicAddress = "127.0.0.1"
        }, NullLogger.Instance);
        gateway.Start();

        using var phone = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var phonePort = ((IPEndPoint)phone.Client.LocalEndPoint!).Port;
        var invite = Invite(phonePort, gateway.SipPort, "0", "PCMU/8000");
        await phone.SendAsync(Encoding.ASCII.GetBytes(invite), new IPEndPoint(IPAddress.Loopback, gateway.SipPort));
        var ok = await ReadSipAsync(phone, "SIP/2.0 200");
        Assert.Contains("SIP/2.0 200", ok, StringComparison.Ordinal);
        Assert.True(gateway.ActiveCalls >= 1);

        var closed = await gateway.HangupAllAsync();
        Assert.True(closed >= 1);
        Assert.Equal(0, gateway.ActiveCalls);
    }

    [Fact]
    public async Task A_call_without_g711_is_refused()
    {
        await using var gateway = new SipAudioGateway(new SipModemOptions
        {
            Enabled = true,
            SipPort = 0,
            RtpPortStart = 0,
            NccPort = 1,
            PublicAddress = "127.0.0.1"
        }, NullLogger.Instance);
        gateway.Start();

        using var phone = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var phonePort = ((IPEndPoint)phone.Client.LocalEndPoint!).Port;
        var invite = Invite(phonePort, gateway.SipPort, "9", "G722/8000");
        await phone.SendAsync(Encoding.ASCII.GetBytes(invite), new IPEndPoint(IPAddress.Loopback, gateway.SipPort));
        var response = await ReadSipAsync(phone, "SIP/2.0 488");
        Assert.Contains("SIP/2.0 488", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_bind_address_is_not_silently_replaced_with_a_wildcard()
    {
        await using var gateway = new SipAudioGateway(new SipModemOptions { BindAddress = "invalid", SipPort = 0 }, NullLogger.Instance);
        Assert.Throws<InvalidOperationException>(() => gateway.Start());
        Assert.False(gateway.Listening);
    }

    private static void AssertLoopbackUdp(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();
        Assert.Contains(listeners, ep => ep.Port == port && ep.Address.Equals(IPAddress.Loopback));
        Assert.DoesNotContain(listeners, ep => ep.Port == port && (ep.Address.Equals(IPAddress.Any) || ep.Address.Equals(IPAddress.IPv6Any)));
    }

    private static string Invite(int fromPort, int toPort, string payloadType, string rtpmap) =>
        "INVITE sip:ncc@127.0.0.1 SIP/2.0\r\n" +
        $"Via: SIP/2.0/UDP 127.0.0.1:{fromPort};branch=z9hG4bK{fromPort};rport\r\n" +
        "From: <sip:lab@127.0.0.1>;tag=lab\r\n" +
        "To: <sip:ncc@127.0.0.1>\r\n" +
        $"Call-ID: lab-{fromPort}@127.0.0.1\r\n" +
        "CSeq: 1 INVITE\r\n" +
        $"Contact: <sip:lab@127.0.0.1:{fromPort}>\r\n" +
        "Content-Type: application/sdp\r\n" +
        "Max-Forwards: 70\r\n" +
        "Content-Length: PLACE\r\n\r\n".Replace("PLACE", Sdp(fromPort, payloadType, rtpmap).Length.ToString()) +
        Sdp(fromPort, payloadType, rtpmap);

    private static string Sdp(int port, string payloadType, string rtpmap) =>
        "v=0\r\n" +
        "o=- 1 1 IN IP4 127.0.0.1\r\n" +
        "s=lab\r\n" +
        "c=IN IP4 127.0.0.1\r\n" +
        "t=0 0\r\n" +
        $"m=audio {port} RTP/AVP {payloadType}\r\n" +
        $"a=rtpmap:{payloadType} {rtpmap}\r\n";

    private static async Task<string> ReadSipAsync(UdpClient phone, string marker)
    {
        var text = "";
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !text.Contains(marker, StringComparison.Ordinal))
        {
            var remaining = deadline - DateTime.UtcNow;
            using var cts = new CancellationTokenSource(remaining);
            var packet = await phone.ReceiveAsync(cts.Token);
            text += Encoding.ASCII.GetString(packet.Buffer);
        }

        return text;
    }

    private static int RtpPort(string sip)
    {
        var line = sip.Split("\r\n").First(row => row.StartsWith("m=audio ", StringComparison.Ordinal));
        return int.Parse(line.Split(' ')[1]);
    }

    private static bool ContainsSequence(List<byte> haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Count < needle.Length)
            return false;
        for (var i = 0; i <= haystack.Count - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }

    private static async Task SendModemAudioAsync(UdpClient phone, int rtpPort, byte[] payload)
    {
        var tx = new Bell202Link();
        tx.Queue(payload);
        var bits = Bell202Link.LeadingMarkBits + payload.Length * 10 + 120;
        var samples = tx.Render((int)Math.Ceiling(bits * Bell202Link.SamplesPerBit) + 320);
        ushort seq = 1;
        uint stamp = 0;
        var remote = new IPEndPoint(IPAddress.Loopback, rtpPort);
        for (var offset = 0; offset < samples.Length; offset += 160)
        {
            var count = Math.Min(160, samples.Length - offset);
            var payloadBytes = new byte[count];
            for (var i = 0; i < count; i++)
            {
                var linear = (short)Math.Clamp((int)Math.Round(samples[offset + i] * 32767f), short.MinValue, short.MaxValue);
                payloadBytes[i] = G711Codec.MuLawEncode(linear);
            }

            var packet = new byte[12 + payloadBytes.Length];
            packet[0] = 0x80;
            packet[1] = 0;
            packet[2] = (byte)(seq >> 8);
            packet[3] = (byte)seq;
            packet[4] = (byte)(stamp >> 24);
            packet[5] = (byte)(stamp >> 16);
            packet[6] = (byte)(stamp >> 8);
            packet[7] = (byte)stamp;
            packet[8] = 1;
            payloadBytes.CopyTo(packet, 12);
            await phone.SendAsync(packet, remote);
            seq++;
            stamp += 160;
            await Task.Delay(20);
        }
    }
}
