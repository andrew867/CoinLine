using System.Net;
using System.Net.Sockets;
using System.Text;
using HostPlatform.Transport;

namespace HostPlatform.SipModem;

/// <summary>
/// Places a loopback G.711 + Bell 202 call against a listening SIP audio modem.
/// Used by the operator self-test. It is not a PSTN call.
/// </summary>
public static class SipModemLabProbe
{
    public static async Task<SipModemSelfTestResult> RunAsync(int sipPort, byte[] payload, CancellationToken ct)
    {
        if (sipPort is <= 0 or >= 65536)
            return new SipModemSelfTestResult { Ok = false, Detail = "SIP modem is not listening." };

        using var phone = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var phonePort = ((IPEndPoint)phone.Client.LocalEndPoint!).Port;
        var invite = Invite(phonePort, sipPort, "0", "PCMU/8000");
        await phone.SendAsync(Encoding.ASCII.GetBytes(invite), new IPEndPoint(IPAddress.Loopback, sipPort), ct);

        var ok = await ReadSipAsync(phone, "SIP/2.0 200", ct);
        if (!ok.Contains("SIP/2.0 200", StringComparison.Ordinal))
        {
            return new SipModemSelfTestResult
            {
                Ok = false,
                Detail = ok.Contains("SIP/2.0 488", StringComparison.Ordinal)
                    ? "The modem refused the call (codec was not G.711)."
                    : "The modem did not answer with 200 OK."
            };
        }

        var rtpPort = RtpPort(ok);
        await SendModemAudioAsync(phone, rtpPort, payload, ct);
        return new SipModemSelfTestResult
        {
            Ok = true,
            Detail = "Loopback G.711 call answered. Bell 202 audio was sent to the NCC listener.",
            BytesSent = payload.Length,
            SipPort = sipPort,
            RtpPort = rtpPort
        };
    }

    private static string Invite(int fromPort, int toPort, string payloadType, string rtpmap) =>
        "INVITE sip:ncc@127.0.0.1 SIP/2.0\r\n" +
        $"Via: SIP/2.0/UDP 127.0.0.1:{fromPort};branch=z9hG4bK{fromPort};rport\r\n" +
        "From: <sip:lab@127.0.0.1>;tag=lab\r\n" +
        "To: <sip:ncc@127.0.0.1>\r\n" +
        $"Call-ID: self-test-{fromPort}@127.0.0.1\r\n" +
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

    private static async Task<string> ReadSipAsync(UdpClient phone, string marker, CancellationToken ct)
    {
        var text = "";
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !text.Contains(marker, StringComparison.Ordinal))
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(remaining);
            try
            {
                var packet = await phone.ReceiveAsync(linked.Token);
                text += Encoding.ASCII.GetString(packet.Buffer);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
        }

        return text;
    }

    private static int RtpPort(string sip)
    {
        var line = sip.Split("\r\n").First(row => row.StartsWith("m=audio ", StringComparison.Ordinal));
        return int.Parse(line.Split(' ')[1]);
    }

    private static async Task SendModemAudioAsync(UdpClient phone, int rtpPort, byte[] payload, CancellationToken ct)
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
            ct.ThrowIfCancellationRequested();
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
            await phone.SendAsync(packet, remote, ct);
            seq++;
            stamp += 160;
            await Task.Delay(20, ct);
        }
    }
}

public sealed class SipModemSelfTestResult
{
    public bool Ok { get; init; }
    public string Detail { get; init; } = "";
    public int BytesSent { get; init; }
    public int SipPort { get; init; }
    public int RtpPort { get; init; }
}

public sealed class SipModemHangupResult
{
    public bool Ok { get; init; }
    public string Detail { get; init; } = "";
    public int Closed { get; init; }
}
