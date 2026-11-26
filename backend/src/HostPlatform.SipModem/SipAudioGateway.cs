using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using HostPlatform.Transport;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace HostPlatform.SipModem;

/// <summary>
/// Answers a SIP call with G.711 only, runs Bell 202 (the minimodem 1200 tone
/// pair) on that audio, and connects the decoded bytes to the NCC listener.
/// Optional REGISTER keeps a credential with a SIP provider so inbound DID
/// calls can reach this gateway. The NCC listener is unchanged.
/// </summary>
public sealed class SipAudioGateway : IAsyncDisposable
{
    private readonly SipModemOptions _options;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ActiveCall> _calls = new();
    private SIPTransport? _transport;
    private IPAddress _bindAddress = IPAddress.Loopback;
    private SIPRegistrationUserAgent? _registration;
    private int _rtpCursor;
    private volatile bool _registered;
    private volatile string? _registrationNote;

    public SipAudioGateway(SipModemOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool Listening { get; private set; }
    public int SipPort { get; private set; }
    public int ActiveCalls => _calls.Count;

    public SipModemSnapshot Snapshot() => new()
    {
        Enabled = _options.Enabled,
        Listening = Listening,
        SipPort = SipPort,
        MinimodemInstalled = MinimodemTool.Find() != null,
        NccPort = _options.NccPort,
        ActiveCalls = ActiveCalls,
        Registrar = string.IsNullOrWhiteSpace(_options.Registrar) ? null : _options.Registrar.Trim(),
        Username = string.IsNullOrWhiteSpace(_options.Username) ? null : _options.Username.Trim(),
        AssignedNumber = string.IsNullOrWhiteSpace(_options.AssignedNumber) ? null : _options.AssignedNumber.Trim(),
        Registered = _registered,
        RegistrationNote = _registrationNote,
        Note = "SIP audio is G.711 into Bell 202 at 1200 baud, the tone pair minimodem uses for 1200. Decoded bytes go to the NCC listener. This is not a hardware modem."
    };

    public void Start()
    {
        if (Listening)
            return;
        if (!IPAddress.TryParse(_options.BindAddress, out var bindAddress))
            throw new InvalidOperationException("SipModem:BindAddress must be a numeric IP address.");
        _bindAddress = bindAddress;
        _transport = new SIPTransport();
        var channel = new SIPUDPChannel(new IPEndPoint(_bindAddress, _options.SipPort));
        _transport.AddSIPChannel(channel);
        SipPort = channel.ListeningSIPEndPoint.GetIPEndPoint().Port;
        _transport.SIPTransportRequestReceived += OnRequest;
        Listening = true;
        _logger.LogInformation("SIP audio modem listening on UDP {Address}:{Port}. Bytes go to {Host}:{NccPort}.", _bindAddress, SipPort, _options.NccHost, _options.NccPort);
        StartRegistration();
    }

    private void StartRegistration()
    {
        var registrar = (_options.Registrar ?? "").Trim();
        var username = (_options.Username ?? "").Trim();
        var password = _options.Password ?? "";
        if (registrar.Length == 0 || username.Length == 0 || password.Length == 0)
        {
            _registrationNote = "No SIP registrar is configured.";
            return;
        }

        if (_transport == null)
            return;

        var publicAddress = string.IsNullOrWhiteSpace(_options.PublicAddress) ? "127.0.0.1" : _options.PublicAddress.Trim();
        var aor = SIPURI.ParseSIPURIRelaxed($"{username}@{registrar}");
        var contact = SIPURI.ParseSIPURIRelaxed($"{username}@{publicAddress}:{SipPort}");
        var expiry = Math.Clamp(_options.RegisterExpirySeconds, 60, 3600);

        // ctor: transport, proxy, aor, authUser, password, realm, registrarHost, contact, …
        _registration = new SIPRegistrationUserAgent(
            _transport,
            null,
            aor,
            username,
            password,
            registrar,
            registrar,
            contact,
            expiry,
            null,
            12,
            30,
            5,
            false);

        _registration.RegistrationSuccessful += (uri, _) =>
        {
            _registered = true;
            _registrationNote = $"Registered as {uri}.";
            _logger.LogInformation("SIP credential registered with {Registrar} as {Uri}.", registrar, uri);
        };
        _registration.RegistrationTemporaryFailure += (_, response, message) =>
        {
            _registered = false;
            var detail = response?.StatusCode.ToString() ?? message;
            _registrationNote = $"Registration retrying: {detail}";
            _logger.LogWarning("SIP registration temporary failure with {Registrar}: {Detail}", registrar, detail);
        };
        _registration.RegistrationFailed += (_, response, message) =>
        {
            _registered = false;
            var detail = response?.StatusCode.ToString() ?? message;
            _registrationNote = $"Registration failed: {detail}";
            _logger.LogWarning("SIP registration failed with {Registrar}: {Detail}", registrar, detail);
        };
        _registration.RegistrationRemoved += (uri, _) =>
        {
            _registered = false;
            _registrationNote = $"Registration removed for {uri}.";
            _logger.LogInformation("SIP registration removed for {Uri}.", uri);
        };

        _registrationNote = $"Registering with {registrar}…";
        _registration.Start();
    }

    private async Task OnRequest(SIPEndPoint local, SIPEndPoint remote, SIPRequest request)
    {
        try
        {
            if (request.Method == SIPMethodsEnum.INVITE)
                await InviteAsync(request);
            else if (request.Method == SIPMethodsEnum.OPTIONS)
                await _transport!.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null));
            else if (request.Method == SIPMethodsEnum.BYE)
            {
                await _transport!.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null));
                await CloseCallAsync(request.Header.CallId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SIP {Method} was not completed.", request.Method);
        }
    }

    private async Task InviteAsync(SIPRequest request)
    {
        if (_transport == null)
            return;
        if (_calls.Count >= Math.Max(1, _options.MaxCalls))
        {
            await _transport.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.BusyHere, null));
            return;
        }

        if (!OffersG711(request.Body, out var preferALaw))
        {
            await _transport.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.NotAcceptableHere, null));
            return;
        }

        var transaction = new UASInviteTransaction(_transport, request, null);
        var uas = new SIPServerUserAgent(_transport, null, transaction, null);
        uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);

        var rtpPort = NextRtpPort();
        var session = new RTPSession(false, false, false, _bindAddress, rtpPort);
        session.AcceptRtpFromAny = true;
        session.addTrack(new MediaStreamTrack(
            preferALaw ? SDPWellKnownMediaFormatsEnum.PCMA : SDPWellKnownMediaFormatsEnum.PCMU));

        var offer = SDP.ParseSDPDescription(request.Body);
        var set = session.SetRemoteDescription(SdpType.offer, offer);
        if (set != SetDescriptionResultEnum.OK)
        {
            uas.Reject(SIPResponseStatusCodesEnum.NotAcceptableHere, null, null);
            session.Close(null);
            return;
        }

        RewriteLoopback(session);

        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            await tcp.ConnectAsync(_options.NccHost, _options.NccPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SIP call could not reach the NCC listener on {Host}:{Port}.", _options.NccHost, _options.NccPort);
            tcp?.Dispose();
            uas.Reject(SIPResponseStatusCodesEnum.TemporarilyUnavailable, null, null);
            session.Close(null);
            return;
        }

        var call = new ActiveCall(request.Header.CallId, tcp, session, preferALaw, uas);
        if (!_calls.TryAdd(call.CallId, call))
        {
            await call.DisposeAsync();
            uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
            return;
        }

        session.OnRtpPacketReceived += (_, media, packet) =>
        {
            if (media != SDPMediaTypesEnum.audio || packet.Payload == null || packet.Payload.Length == 0)
                return;
            var aLaw = packet.Header.PayloadType == 8;
            var pcm = new float[packet.Payload.Length];
            for (var i = 0; i < packet.Payload.Length; i++)
                pcm[i] = G711Codec.Decode(packet.Payload[i], aLaw) / 32768f;
            var bytes = call.FromPhone.Push(pcm);
            if (bytes.Length > 0)
            {
                try
                {
                    call.Stream.Write(bytes);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "NCC write failed for SIP call {CallId}.", call.CallId);
                }
            }
        };

        IPAddress.TryParse(_options.PublicAddress, out var advertised);
        var answer = session.CreateAnswer(advertised);
        uas.Answer(SDP.SDP_MIME_CONTENTTYPE, answer.ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);
        await session.Start();
        uas.CallCancelled += (_, _) => _ = CloseCallAsync(call.CallId);
        session.OnRtpClosed += reason =>
        {
            _ = CloseCallAsync(call.CallId);
        };
        _ = PumpToPhoneAsync(call);
        _logger.LogInformation("SIP call {CallId} is on the NCC listener.", call.CallId);
    }

    private async Task PumpToPhoneAsync(ActiveCall call)
    {
        var scratch = new byte[256];
        try
        {
            while (!call.Stopped.IsCancellationRequested)
            {
                if (call.Stream.DataAvailable)
                {
                    var n = await call.Stream.ReadAsync(scratch, call.Stopped.Token);
                    if (n > 0)
                        call.ToPhone.Queue(scratch.AsSpan(0, n));
                    else
                        break;
                }

                var samples = call.ToPhone.Render(160);
                var encoded = new byte[samples.Length];
                for (var i = 0; i < samples.Length; i++)
                {
                    var linear = (short)Math.Clamp((int)Math.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
                    encoded[i] = G711Codec.Encode(linear, call.ALaw);
                }

                call.Session.AudioStream?.SendAudioFrame(160, call.ALaw ? 8 : 0, encoded);
                await Task.Delay(20, call.Stopped.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SIP audio toward the phone stopped for {CallId}.", call.CallId);
        }
    }

    private void RewriteLoopback(RTPSession session)
    {
        if (string.IsNullOrWhiteSpace(_options.LoopbackHost))
            return;
        var dest = session.AudioDestinationEndPoint;
        if (dest == null || !IPAddress.IsLoopback(dest.Address))
            return;
        IPAddress? host = null;
        try
        {
            host = Dns.GetHostAddresses(_options.LoopbackHost)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve {Host} for SIP audio.", _options.LoopbackHost);
        }

        if (host == null)
            return;
        var rtp = new IPEndPoint(host, dest.Port);
        var rtcpPort = session.AudioControlDestinationEndPoint?.Port ?? dest.Port + 1;
        session.SetDestination(SDPMediaTypesEnum.audio, rtp, new IPEndPoint(host, rtcpPort));
    }

    private int NextRtpPort()
    {
        if (_options.RtpPortStart <= 0)
            return 0;
        var slot = Interlocked.Increment(ref _rtpCursor);
        var width = Math.Max(1, _options.MaxCalls);
        return _options.RtpPortStart + ((slot - 1) % width) * 2;
    }

    private static bool OffersG711(string? body, out bool preferALaw)
    {
        preferALaw = false;
        if (string.IsNullOrWhiteSpace(body))
            return false;
        var pcmu = body.Contains("PCMU", StringComparison.OrdinalIgnoreCase) || body.Contains("RTP/AVP 0", StringComparison.Ordinal);
        var pcma = body.Contains("PCMA", StringComparison.OrdinalIgnoreCase);
        preferALaw = pcma && !pcmu;
        return pcmu || pcma;
    }

    private async Task CloseCallAsync(string? callId)
    {
        if (string.IsNullOrEmpty(callId) || !_calls.TryRemove(callId, out var call))
            return;
        await call.DisposeAsync();
    }

    /// <summary>Ends every active SIP call on this gateway. Used by the operator clear action and self-test cleanup.</summary>
    public async Task<int> HangupAllAsync()
    {
        var ids = _calls.Keys.ToArray();
        var closed = 0;
        foreach (var id in ids)
        {
            if (_calls.TryRemove(id, out var call))
            {
                await call.DisposeAsync();
                closed++;
            }
        }

        return closed;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _registration?.Stop(true);
        }
        catch (Exception)
        {
        }

        _registration = null;
        _registered = false;
        await HangupAllAsync();
        _transport?.Shutdown();
        Listening = false;
    }

    private sealed class ActiveCall : IAsyncDisposable
    {
        public ActiveCall(string callId, TcpClient client, RTPSession session, bool aLaw, SIPServerUserAgent agent)
        {
            CallId = callId;
            Client = client;
            Stream = client.GetStream();
            Session = session;
            ALaw = aLaw;
            Agent = agent;
        }

        public string CallId { get; }
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public RTPSession Session { get; }
        public bool ALaw { get; }
        public SIPServerUserAgent Agent { get; }
        public Bell202Link FromPhone { get; } = new();
        public Bell202Link ToPhone { get; } = new();
        public CancellationTokenSource Stopped { get; } = new();

        public async ValueTask DisposeAsync()
        {
            Stopped.Cancel();
            try
            {
                if (!Agent.IsHungup)
                    Agent.Hangup(false);
            }
            catch (Exception)
            {
            }

            Session.Close(null);
            try
            {
                Client.Close();
            }
            catch (Exception)
            {
            }

            await Task.CompletedTask;
        }
    }
}
