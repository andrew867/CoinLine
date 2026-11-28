using System.Diagnostics;
using System.Data;
using System.Net.Sockets;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Security;
using HostPlatform.Api.Services;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Rating;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Hosting;

/// <summary>
/// One modem socket. Records accepted frames with sensitive card data omitted, replies
/// to runtime requests, and pushes the assigned install set on an explicit request.
/// A control acknowledgement has no terminal-id field; the session row stores the five bytes.
/// </summary>
internal sealed class NccLinkProcessor(
    HostPlatformDbContext db,
    TableDistributionService tables,
    DlogTransactionEngine dlog,
    INccSessionOutbound outbound,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger logger,
    CancellationToken stoppingToken,
    INccCardAuthorizationProvider? cardProvider,
    NccListenerSettings settings)
{
    private const string Actor = "ncc-listener";
    private const byte EndDataMessageType = 13;
    private const byte TableAckMessageType = 14;
    private const byte TableUpdateMessageType = 18;
    private const byte TableDownloadRequestType = DlogTableRequestWire.MessageType;
    private const byte RoutineCallInMessageType = 8;
    private const int MaxInstallRetransmissions = 2;
    private static readonly TimeSpan AckWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FirmwareLinkAckDelay = TimeSpan.FromMilliseconds(350);

    private NccSession _session = null!;
    private int _sequence;
    private int _lastReceivedSequence;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _captureOrder = new();
    private byte _nextMessagePacketId;
    private NccWirePacket? _lastInboundMessage;
    private NccWirePacket? _lastTimeSync;
    private NccWirePacket? _lastDialogueReply;
    private NccWirePacket? _lastDialogueRequest;
    private Guid? _callUploadPhoneId;
    private NccWirePacket? _lastCallUploadEndRequest;
    private bool _timeSyncAwaitingAck;
    private DownloadBatch? _batch;
    private ScheduledTableDownload? _scheduledDownload;
    private TerminalIdentityCutoverItem? _identityCutover;
    private bool _installStarted;
    private bool _mandatoryFullInstall;
    private bool _firmwareDialogue;
    private volatile bool _terminalCleared;
    private readonly Dictionary<int, DownloadBatchItem> _waiting = new();
    private FirmwareTableTransfer? _firmwareTables;
    private CallbackOffer? _callbackOffer;
    private bool _callbackOffered;

    private bool HasPendingInstall => _waiting.Count > 0 || _firmwareTables != null;

    public async Task RunAsync(TcpClient client)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _session = new NccSession
        {
            Status = NccSessionStatus.Active,
            StartedAtUtc = DateTime.UtcNow,
            CorrelationId = "ncc-" + Guid.NewGuid().ToString("N")[..12],
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CreatedBy = Actor,
            UpdatedBy = Actor
        };
        db.NccSessions.Add(_session);
        Audit("session_open", new { correlationId = _session.CorrelationId }, null);
        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NCC session row was not stored.");
        }

        var decoder = new NccIncrementalStreamDecoder(NccParseMode.Strict);
        var stream = client.GetStream();
        var buf = new byte[1024];
        var lastFrameAt = Stopwatch.GetTimestamp();
        long? partialFrameAt = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                // A partial frame or unrelated bytes do not renew the dialogue deadline.
                var idleLeft = settings.IdleTimeout - Stopwatch.GetElapsedTime(lastFrameAt);
                var timeout = Min(settings.ReadTimeout, idleLeft);
                if (partialFrameAt is { } started)
                    timeout = Min(timeout, settings.ReadTimeout - Stopwatch.GetElapsedTime(started));
                if (HasPendingInstall || _callbackOffer != null)
                    timeout = Min(timeout, AckWait - Stopwatch.GetElapsedTime(lastFrameAt));
                if (timeout <= TimeSpan.Zero)
                    readCts.Cancel();
                else
                    readCts.CancelAfter(timeout);

                int n;
                try
                {
                    readCts.Token.ThrowIfCancellationRequested();
                    n = await stream.ReadAsync(buf, readCts.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested && HasPendingInstall)
                {
                    var firmwareTransfer = _firmwareTables != null;
                    await FailOutstandingAsync("acknowledgement was missing", missingAcknowledgement: true);
                    if (firmwareTransfer || Stopwatch.GetElapsedTime(lastFrameAt) >= settings.IdleTimeout ||
                        (partialFrameAt != null && Stopwatch.GetElapsedTime(partialFrameAt.Value) >= settings.ReadTimeout))
                        break;
                    continue;
                }
                catch (OperationCanceledException)
                {
                    if (!stoppingToken.IsCancellationRequested)
                        logger.LogInformation("NCC session {SessionId} reached its receive deadline.", _session.Id);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogInformation(ex, "NCC read ended for {Remote}.", remote);
                    break;
                }

                if (n == 0)
                    break;

                List<(NccStreamParsedFrame Parsed, NccSessionFrame? Frame)> arrivals;
                try
                {
                    // Reserve received frames before handling any of them, since one read can carry several.
                    lock (_captureOrder)
                    {
                        arrivals = [];
                        foreach (var item in decoder.Feed(buf.AsSpan(0, n)))
                        {
                            if (item is not NccStreamParsedFrame parsed || parsed.Frame.IsTruncated)
                                continue;
                            var accepted = parsed.Frame.Parse.Packet is { } candidate
                                ? CaptureFrame(candidate.Control, parsed.Frame.RawBytes.Length, sentByHost: false)
                                : null;
                            arrivals.Add((parsed, accepted));
                        }
                        if (decoder.PendingByteCount == 0)
                            partialFrameAt = null;
                        else if (partialFrameAt == null || arrivals.Count > 0)
                            partialFrameAt = Stopwatch.GetTimestamp();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "NCC decode failed.");
                    continue;
                }

                foreach (var (parsed, frame) in arrivals)
                {
                    var parse = parsed.Frame.Parse;
                    if (!parse.Success || parse.Packet is not { } packet)
                    {
                        if (parse.Packet is { } retry && IsHeaderOnlyRetransmission(retry, parsed.Frame.RawBytes))
                        {
                            await AcceptFrameAsync(stream, retry, parsed.Frame.RawBytes, frame!, headerOnlyRetry: true);
                            lastFrameAt = Stopwatch.GetTimestamp();
                            logger.LogInformation("NCC header-only retransmission acknowledged for session {SessionId}.", _session.Id);
                        }
                        else
                        {
                            logger.LogInformation("NCC frame rejected for session {SessionId}: {Reason}.",
                                _session.Id, string.Join("; ", parse.Diagnostics));
                        }
                        continue;
                    }

                    var terminalClear = (packet.Control & NccControl.Clr) != 0;
                    if (terminalClear)
                    {
                        // The terminal has ended the application dialogue, but its modem may
                        // still be completing the normal local clear and relay-release sequence.
                        _terminalCleared = true;
                        outbound.Unregister(_session.Id);
                    }
                    await AcceptFrameAsync(stream, packet, parsed.Frame.RawBytes, frame!);
                    lastFrameAt = Stopwatch.GetTimestamp();
                    if (terminalClear)
                    {
                        await DrainAfterTerminalClearAsync(stream, buf);
                        return;
                    }
                }
            }
        }
        catch (IOException ex)
        {
            logger.LogInformation("NCC link ended for session {SessionId}. {Reason}", _session.Id, ex.Message);
        }
        finally
        {
            if (_batch?.Status == DownloadBatchStatus.Running)
                await FailOutstandingAsync("acknowledgement was missing", missingAcknowledgement: true);

            outbound.Unregister(_session.Id);

            _session.Status = NccSessionStatus.Closed;
            _session.EndedAtUtc = DateTime.UtcNow;
            _session.UpdatedAtUtc = DateTime.UtcNow;
            Audit("session_close", new { correlationId = _session.CorrelationId }, _session.TerminalId);
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NCC session close was not stored.");
            }

            client.Dispose();
            logger.LogInformation("NCC link closed {Remote}.", remote);
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private async Task DrainAfterTerminalClearAsync(NetworkStream stream, byte[] buffer)
    {
        // Don't send another application frame, ACK, FIN or carrier-drop signal in
        // response to CLR. Allow the peer to release first, but never hold the
        // listener slot indefinitely. Bytes after clear are not another dialogue.
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        drain.CancelAfter(settings.TerminalClearDrainTimeout);
        var started = Stopwatch.GetTimestamp();
        var outcome = "peer_eof";
        long discardedBytes = 0;
        try
        {
            while (true)
            {
                drain.Token.ThrowIfCancellationRequested();
                var count = await stream.ReadAsync(buffer, drain.Token);
                if (count == 0) break;
                discardedBytes += count;
            }
        }
        catch (OperationCanceledException)
        {
            outcome = stoppingToken.IsCancellationRequested ? "shutdown" : "deadline";
        }
        catch (IOException) { outcome = "peer_error"; }
        catch (SocketException) { outcome = "peer_error"; }
        Audit("terminal_clear_drain", new { outcome, discardedBytes,
            elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds }, _session.TerminalId);
        logger.LogInformation("NCC terminal clear drained for session {SessionId}: {Outcome}.", _session.Id, outcome);
    }

    private bool IsHeaderOnlyRetransmission(NccWirePacket packet, ReadOnlySpan<byte> raw) =>
        _lastInboundMessage is { } previous
        && _session.TerminalId != null
        && packet.Control == (NccControl.PacketId(previous.Control) | NccControl.ReTrans)
        && packet.Count == NccConstants.ControlPayloadOverhead + NccConstants.TerminalIdSize
        && packet.Data.Length == 0
        && packet.TerminalId.AsSpan().SequenceEqual(previous.TerminalId)
        && packet.FrameStart == NccConstants.FrameStart
        && packet.FrameEnd == NccConstants.FrameEnd
        && packet.Crc == NccCrc16.Compute(raw[..^3]);

    private async Task AcceptFrameAsync(NetworkStream stream, NccWirePacket packet, byte[] raw, NccSessionFrame frame,
        bool headerOnlyRetry = false)
    {
        _lastReceivedSequence = frame.Sequence;
        var control = NccControl.IsControlPacket(packet.Control);
        var hex = control ? string.Empty : Convert.ToHexString(packet.TerminalId);
        var terminal = control ? null : await MatchTerminalAsync(hex, frame.CreatedAtUtc);
        var repeatedUploadEnd = !control && !headerOnlyRetry
            && (packet.Control & NccControl.ReTrans) != 0
            && _lastCallUploadEndRequest is { } previousEnd
            && ReferenceEquals(_lastInboundMessage, previousEnd)
            && ReferenceEquals(_lastDialogueRequest, previousEnd)
            && _lastDialogueReply != null
            && NccControl.PacketId(previousEnd.Control) == NccControl.PacketId(packet.Control)
            && previousEnd.TerminalId.AsSpan().SequenceEqual(packet.TerminalId)
            && previousEnd.Data.AsSpan().SequenceEqual(packet.Data);
        var repeatedMaintenance = !control && !headerOnlyRetry && (packet.Control & NccControl.ReTrans) != 0
            && _lastDialogueRequest is { } previousMaintenance && ReferenceEquals(_lastInboundMessage, previousMaintenance)
            && _lastDialogueReply != null && DlogMaintenanceWire.HasRequestRecord(previousMaintenance.Data)
            && NccControl.PacketId(previousMaintenance.Control) == NccControl.PacketId(packet.Control)
            && previousMaintenance.TerminalId.AsSpan().SequenceEqual(packet.TerminalId)
            && previousMaintenance.Data.AsSpan().SequenceEqual(packet.Data);
        var repeatedAlarm = !control && !headerOnlyRetry && terminal != null && (packet.Control & NccControl.ReTrans) != 0
            && _lastDialogueRequest is { } previousAlarm && ReferenceEquals(_lastInboundMessage, previousAlarm)
            && _lastDialogueReply != null && DlogAlarmWire.TryBuildAcknowledgement(previousAlarm.Data, out _)
            && NccControl.PacketId(previousAlarm.Control) == NccControl.PacketId(packet.Control)
            && previousAlarm.TerminalId.AsSpan().SequenceEqual(packet.TerminalId)
            && previousAlarm.Data.AsSpan().SequenceEqual(packet.Data);
        if (!control)
        {
            _session.TerminalId = terminal?.Id;
            if (!headerOnlyRetry)
                _lastInboundMessage = terminal == null ? null : packet;
        }
        var messageType = !control && packet.Data.Length > 0 ? packet.Data[0] : (byte?)null;
        var cardAuthorization = messageType is { } type && DlogCardAuthorizationWire.IsRequest(type);
        var callDetails = messageType is { } detailType && DlogCallDetailWire.IsRecord(detailType);
        var rateRequest = messageType == DlogRateWire.MessageTypeRequest;
        var immediateRequestLength = 0;
        IReadOnlyList<Range> immediateCallDetails = [];
        var immediateBundleValid = (cardAuthorization || rateRequest) && DlogImmediateRequestWire.TrySplit(packet.Data,
            out immediateRequestLength, out immediateCallDetails);
        var uploadAttention = messageType == DlogCallDetailWire.UploadAttention && packet.Data.Length >= 2;
        var explicitUpload = _callUploadPhoneId != null && !control && !headerOnlyRetry && !uploadAttention;
        DlogCallUploadWire.Frame? uploadFrame = null;
        if (explicitUpload)
            DlogCallUploadWire.TryParse(packet.Data, out uploadFrame);
        var reports = DlogTerminalStatisticsWire.FindReports(packet.Data, out var truncatedReport);
        if (uploadFrame != null)
        {
            reports = uploadFrame.Statistics.ToList();
            truncatedReport = false;
        }
        var statistics = reports.Count > 0 || truncatedReport;
        var cashboxReports = DlogCashboxWire.FindReports(packet.Data, out var truncatedCashbox);
        if (uploadFrame != null)
        {
            cashboxReports = uploadFrame.Cashbox.ToList();
            truncatedCashbox = false;
            callDetails = uploadFrame.CallDetails.Count > 0;
        }
        var hasCashboxCollection = false;
        // A rate request can carry a call record after its fixed-length header.
        var recordedBytes = explicitUpload || repeatedUploadEnd || cardAuthorization || callDetails || statistics || cashboxReports.Count > 0 || truncatedCashbox || messageType == 6 ||
            (rateRequest && packet.Data.Length > DlogRateWire.RequestLength) ? Array.Empty<byte>() : raw;
        _session.LastFrameSample = recordedBytes;
        _session.UpdatedAtUtc = DateTime.UtcNow;
        _session.UpdatedBy = Actor;

        if (terminal != null && terminal.DeactivatedAtUtc == null)
            outbound.Register(terminal.Id, _session.Id, packet.TerminalId.ToArray(),
                (bytes, ct) => WriteCapturedFrameAsync(stream, bytes, ct));

        frame.TerminalIdHex = hex;
        frame.TerminalMatched = terminal != null;
        frame.RawBytes = recordedBytes;
        db.NccSessionFrames.Add(frame);
        Audit("frame_stored", new
        {
            correlationId = _session.CorrelationId,
            terminalIdHex = hex,
            matched = terminal != null,
            unknown = !control && terminal == null,
            redactedMessageType = recordedBytes.Length == 0 ? messageType : null,
            immediateRequest = cardAuthorization || rateRequest ? new
            {
                applicationBytes = packet.Data.Length,
                validBundle = immediateBundleValid,
                bundledCallRecords = immediateBundleValid ? (int?)immediateCallDetails.Count : null
            } : null,
            callUploadEnd = uploadFrame?.EndsDialogue,
            callUpload = explicitUpload ? new
            {
                sequence = frame.Sequence,
                parsed = uploadFrame != null,
                endsDialogue = uploadFrame?.EndsDialogue,
                records = uploadFrame?.Records.Select(record => new
                {
                    messageType = record.MessageType,
                    byteCount = record.ByteCount
                }).ToArray()
            } : null
        }, _session.TerminalId);

        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NCC frame was not stored.");
        }

        if (_session.TerminalId is { } boundTerminalId && await db.Terminals.AsNoTracking()
                .AnyAsync(phone => phone.Id == boundTerminalId && phone.DeactivatedAtUtc != null, stoppingToken))
        {
            throw new IOException("NCC link closed before acknowledgement: terminal is deactivated.");
        }

        if (control)
        {
            if (_callbackOffer != null)
                await HandleCallbackControlAsync(stream, packet.Control);
            else if (_firmwareTables != null)
                await HandleFirmwareTableControlAsync(stream, packet.Control);
            else
                await HandleTimeSyncControlAsync(stream, packet.Control);
            return;
        }

        if (repeatedUploadEnd)
        {
            await Task.Delay(FirmwareLinkAckDelay, stoppingToken);
            await WriteAckAsync(stream, packet.Control);
            if (await SendDialogueReplyAsync(stream, packet, _lastDialogueReply!.Data, "call upload completion retry"))
                _lastCallUploadEndRequest = packet;
            return;
        }

        if (repeatedAlarm)
        {
            // A complete identical retry replays its original application receipt,
            // not the event ledger or its destination decision.
            if (_firmwareDialogue) await Task.Delay(FirmwareLinkAckDelay, stoppingToken);
            await WriteAckAsync(stream, packet.Control);
            await SendDialogueReplyAsync(stream, packet, _lastDialogueReply!.Data, "alarm retry");
            return;
        }

        if (explicitUpload && (terminal == null || terminal.Id != _callUploadPhoneId || uploadFrame == null))
            throw new IOException("NCC call upload closed before acknowledgement: unrecognized or incomplete upload record.");

        var maintenanceRequest = DlogMaintenanceWire.HasRequestRecord(packet.Data);
        var callIn = packet.Data is [RoutineCallInMessageType] or [DlogCallbackWire.Return];
        if (cashboxReports.Count > 0 || truncatedCashbox)
        {
            if (terminal == null || truncatedCashbox)
                throw new IOException("Cashbox upload could not be acknowledged without a registered phone and complete records.");

            try
            {
                foreach (var range in cashboxReports)
                {
                    var record = packet.Data[range];
                    await dlog.IngestCashboxAsync(record, terminal.Id, _session.Id, _session.CorrelationId, stoppingToken);
                    hasCashboxCollection |= record[0] == DlogCashboxWire.CollectionType;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new IOException("Cashbox upload was not stored.", ex);
            }
        }
        if (statistics)
        {
            if (terminal == null || truncatedReport)
            {
                // Without a link ACK, the terminal retains its reports for another call.
                var reason = truncatedReport ? "truncated statistics report" : "unregistered statistics source";
                throw new IOException($"NCC link closed before acknowledgement: {reason}.");
            }

            try
            {
                foreach (var reportRange in reports)
                {
                    var report = DlogTerminalStatisticsWire.Decode(packet.Data.AsSpan()[reportRange], out var fingerprint);
                    await TerminalUploads.StoreAsync(db, terminal.Id, _session.Id, report, fingerprint, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new IOException($"Statistics upload was not stored ({ex.GetType().Name}).", ex);
            }
        }
        var receipts = callDetails && terminal != null
            ? await StoreCallDetailsAsync(packet.Data, terminal, uploadFrame?.CallDetails) : [];
        if (uploadFrame != null)
        {
            if (receipts.Length != uploadFrame.CallDetails.Count * 3 ||
                !await RecordFieldRecordsAsync(uploadFrame.Status.Select(range => packet.Data[range]), terminal))
                throw new IOException("NCC call upload closed before acknowledgement: complete records were not stored.");
        }
        var immediateReceipts = immediateBundleValid && immediateCallDetails.Count > 0 && terminal != null
            ? await StoreCallDetailsAsync(packet.Data, terminal, immediateCallDetails) : [];
        if (immediateBundleValid && immediateCallDetails.Count > 0 && immediateReceipts.Length != immediateCallDetails.Count * 3)
            throw new IOException("NCC immediate-clear dialogue closed before acknowledgement: bundled call records were not stored.");
        if (terminal != null)
        {
            _firmwareDialogue |= maintenanceRequest || rateRequest || cardAuthorization || callIn || callDetails || uploadAttention || explicitUpload || statistics || cashboxReports.Count > 0
                || packet.Data is [DlogTimeSyncWire.Request] or [TableDownloadRequestType, _, ..];
            // Give the terminal time to arm its reply timer after transmit completion.
            if (_firmwareDialogue)
                await Task.Delay(FirmwareLinkAckDelay, stoppingToken);
        }

        await WriteAckAsync(stream, packet.Control);
        if (headerOnlyRetry)
        {
            if (_lastDialogueReply is { } reply && ReferenceEquals(_lastInboundMessage, _lastDialogueRequest)
                && (reply.Data[0] is DlogCallDetailWire.Acknowledgement or DlogCallDetailWire.RequestUpload ||
                    _lastCallUploadEndRequest != null && ReferenceEquals(_lastDialogueRequest, _lastCallUploadEndRequest) ||
                    _lastDialogueRequest!.Data is [DlogCashboxWire.CollectionType, ..] or [TableDownloadRequestType, DlogCashboxWire.StatusRequestFlag, ..]
                        or [DlogCallDetailWire.UploadAttention, _, ..]
                    || DlogMaintenanceWire.HasRequestRecord(_lastDialogueRequest.Data)
                    || DlogAlarmWire.TryBuildAcknowledgement(_lastDialogueRequest.Data, out _)
                    || (_lastDialogueRequest.Data.Length > 0 && (DlogCardAuthorizationWire.IsRequest(_lastDialogueRequest.Data[0])
                        || _lastDialogueRequest.Data[0] == DlogRateWire.MessageTypeRequest))))
                await SendDialogueReplyAsync(stream, packet, reply.Data, "immediate dialogue retry");
            return;
        }

        if (uploadFrame != null)
        {
            if (uploadFrame.EndsDialogue)
            {
                // MT13 is the terminal's application-level end marker, not a link clear.
                // Answer it only after all recognized upload records have been committed.
                if (await SendDialogueReplyAsync(stream, packet, [.. receipts, EndDataMessageType], "call upload completion"))
                {
                    _callUploadPhoneId = null;
                    _lastCallUploadEndRequest = packet;
                }
            }
            else if (receipts.Length > 0)
                await SendDialogueReplyAsync(stream, packet, receipts, "call detail");
            return;
        }

        if (callDetails)
        {
            if (receipts.Length > 0)
                await SendDialogueReplyAsync(stream, packet, receipts, "call detail");
            return;
        }

        if (hasCashboxCollection && messageType == DlogCashboxWire.CollectionType)
        {
            await SendDialogueReplyAsync(stream, packet, [EndDataMessageType], "cashbox collection");
            return;
        }

        if (packet.Data is [DlogTimeSyncWire.Request])
        {
            if (terminal != null)
                await SendTimeSyncAsync(stream, packet);
            return;
        }

        if (maintenanceRequest)
        {
            var complete = DlogMaintenanceWire.TryBuildAcknowledgement(packet.Data, out var reply);
            if (!repeatedMaintenance)
            {
                var matches = terminal != null && complete ? await MatchMaintenanceReportsAsync(packet.Data) : null;
                await RecordFieldEventAsync(packet.Data, terminal, matches);
            }
            if (terminal != null && complete)
                await SendManagementReplyAsync(stream, packet, terminal.Id, reply, "maintenance");
            return;
        }

        if (rateRequest)
        {
            await TryRateAsync(stream, packet, immediateReceipts);
            return;
        }

        if (cardAuthorization)
        {
            if (terminal != null)
                await AuthorizeCardAsync(stream, packet, terminal.Id, immediateRequestLength, immediateBundleValid, immediateReceipts);
            return;
        }

        if (messageType is not { } applicationType)
            return;

        if (packet.Data.Length >= 2 && applicationType == TableAckMessageType)
            await ApplyTableAckAsync(stream, packet.Data[1], raw);
        else if (FieldEventCatalog.TryDescribe(applicationType, out _))
        {
            // Only pass the status record itself to the event ledger when statistics follow it.
            var fieldData = statistics ? packet.Data[..DlogTerminalStatisticsWire.StatusRecordLength(applicationType)] : packet.Data;
            var stored = await RecordFieldEventAsync(fieldData, terminal);
            if (stored && terminal != null && applicationType == DlogAlarmWire.AlarmMessageType
                && DlogAlarmWire.TryBuildAcknowledgement(packet.Data, out var acknowledgement))
                await SendManagementReplyAsync(stream, packet, terminal.Id, acknowledgement, "alarm");
            else if (stored && terminal != null && uploadAttention)
            {
                _callUploadPhoneId = terminal.Id;
                _lastCallUploadEndRequest = null;
                if (TryImmediateReplyRetry(packet, out var previousReply))
                {
                    await SendDialogueReplyAsync(stream, packet, previousReply.Data, "call upload request retry");
                    return;
                }

                var statusRequested = (packet.Data[1] & DlogCashboxWire.StatusRequestFlag) != 0;
                var offer = statusRequested ? await TerminalCashboxAccounting.ReadCurrentStatusOfferAsync(db, terminal.Id, stoppingToken) : null;
                var status = offer?.Bytes;
                // MT38 is a complete status record. MT17 starts the terminal's
                // upload, so neither a table-update marker nor EOD belongs here.
                // An unknown current box must never receive a fabricated status.
                byte[] response = status == null ? [DlogCallDetailWire.RequestUpload] : [..status, DlogCallDetailWire.RequestUpload];
                var sent = await SendDialogueReplyAsync(stream, packet, response, "call upload request");
                if (statusRequested)
                {
                    Audit("call_upload_cashbox_restore", new { available = status != null, sent, responseBytes = response.Length, source = offer?.Source }, terminal.Id);
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
        }

        if (terminal != null && (callIn || applicationType == TableDownloadRequestType && packet.Data.Length >= 2))
        {
            byte[]? cashboxStatus = null;
            if (applicationType == TableDownloadRequestType && (packet.Data[1] & DlogCashboxWire.StatusRequestFlag) != 0)
            {
                cashboxStatus = await TerminalCashboxAccounting.ReadCurrentStatusBytesAsync(db, terminal.Id, stoppingToken);
                if (cashboxStatus == null && (packet.Data[1] & DlogTableRequestWire.Install) != 0)
                {
                    _ = await TerminalCashboxAccounting.InitializeDefaultAsync(db, terminal.Id, "ncc-install", firstInstall: true, stoppingToken);
                    cashboxStatus = await TerminalCashboxAccounting.ReadCurrentStatusBytesAsync(db, terminal.Id, stoppingToken);
                }
                if (packet.Data[1] == DlogCashboxWire.StatusRequestFlag)
                {
                    await SendDialogueReplyAsync(stream, packet, InstallEndPayload(cashboxStatus), "cashbox status request");
                    return;
                }
            }
            var mandatoryFull = applicationType == TableDownloadRequestType && DlogTableRequestWire.RequiresFullAssignment(packet.Data[1]);
            var isCallbackReturn = applicationType == DlogCallbackWire.Return;
            // A phone can return an acknowledged callback after ending a separate
            // Craft download on this same socket. Do not consume a callback while
            // a transfer is still active or an identity transition owns the link.
            var completedEarlierDownload = _installStarted && !HasPendingInstall &&
                _batch?.Status == DownloadBatchStatus.Completed && _identityCutover == null;
            var returned = isCallbackReturn && (!_installStarted || completedEarlierDownload)
                ? await RecordCallbackReturnAsync(terminal.Id) : null;
            if (returned != null && completedEarlierDownload)
            {
                // Retain the completed batch in storage, but release only the
                // per-dialogue state so this genuine, distinct return can claim
                // its own queued work. Duplicate or unacknowledged MT9 requests
                // do not reset the transfer guard.
                _batch = null;
                _scheduledDownload = null;
                _mandatoryFullInstall = false;
                _installStarted = false;
            }
            var ordinaryCraft = applicationType == TableDownloadRequestType && DlogTableRequestWire.IsOrdinaryCraftUpdate(packet.Data[1]);
            var continuation = callIn ? CallbackContinuation.CallIn : mandatoryFull ? CallbackContinuation.FullInstall :
                ordinaryCraft ? CallbackContinuation.CraftUpdate : CallbackContinuation.Install;
            if (returned == null && await TryOfferCallbackAsync(stream, terminal.Id, packet.TerminalId, continuation, cashboxStatus: cashboxStatus))
                return;
            await StartInstallAsync(stream, packet.TerminalId, terminal, callIn, returned?.DeliverAssignedTables == true, cashboxStatus,
                mandatoryFull, allowCutover: callIn || applicationType == TableDownloadRequestType &&
                    (packet.Data[1] & DlogTableRequestWire.CraftUpdate) != 0,
                distinctCallbackDialogue: returned != null && completedEarlierDownload, allowAutomaticChangedRecords: ordinaryCraft);
        }
    }

    private async Task AuthorizeCardAsync(NetworkStream stream, NccWirePacket request, Guid terminalId,
        int requestLength, bool bundleValid, byte[] receipts)
    {
        var providerKind = cardProvider switch
        {
            SyntheticLabCardAuthorizationProvider => CardAuthorizationAudit.LabProvider,
            ShadyBankCardAuthorizationProvider => CardAuthorizationAudit.ShadyBankProvider,
            null => "none",
            _ => "trusted_provider"
        };
        // A retry of the same dialogue does not create a second authorization.
        if (TryImmediateReplyRetry(request, out var reply))
        {
            await SendDialogueReplyAsync(stream, request, reply.Data, "card authorization retry");
            return;
        }

        var protectedRequest = requestLength > 0 && request.Data.Length >= requestLength
            ? request.Data.AsMemory(0, requestLength) : ReadOnlyMemory<byte>.Empty;
        var canDecline = DlogCardAuthorizationWire.TryBuildDecline(protectedRequest.Span, out var decline);
        if (!bundleValid || !canDecline)
        {
            var suffixTypes = DlogImmediateRequestWire.RecognizedSuffixTypes(request.Data, requestLength).Select(type => (int)type).ToArray();
            logger.LogWarning("Malformed card authorization bundle: {RequestBytes} bytes; recognized suffix CDR types [{SuffixTypes}].",
                request.Data.Length, string.Join(",", suffixTypes));
            await StoreCardAttemptAsync(NewCardAttempt(terminalId,
                new CardAuthorizationAuditDetail(request.Data[0], "malformed_request", providerKind, RequestBytes: request.Data.Length,
                    SuffixCdrTypes: suffixTypes)));
            // A complete known request header permits an explicit denial even if
            // its suffix is malformed. Never invoke the provider or acknowledge
            // records from an invalid bundle.
            if (canDecline) await SendDialogueReplyAsync(stream, request, decline, "malformed card authorization decline");
            return;
        }

        var response = decline;
        var outcome = "provider_unavailable";
        SyntheticAuthorizationEvidence? evidence = null;
        ShadyBankAuthorizationEvidence? bankEvidence = null;
        if (cardProvider != null)
        {
            using var authorizationDeadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            authorizationDeadline.CancelAfter(settings.CardAuthorizationTimeout);
            var deadlineToken = authorizationDeadline.Token;
            // Separate scope: a provider that ignores cancellation cannot keep using
            // the session's DbContext while its deadline decline is being recorded.
            var invocation = Task.Run(async () =>
            {
                await using var scope = scopes.CreateAsyncScope();
                deadlineToken.ThrowIfCancellationRequested();
                var provider = scope.ServiceProvider.GetRequiredService<INccCardAuthorizationProvider>();
                return await provider.AuthorizeAsync(terminalId, protectedRequest, deadlineToken);
            }, CancellationToken.None);
            try
            {
                var decision = await invocation.WaitAsync(deadlineToken);
                if (decision != null && DlogCardAuthorizationWire.TryBuildDecision(protectedRequest.Span, decision.Approved,
                        decision.AuthorizationReference.Span, out var encoded))
                {
                    response = encoded;
                    outcome = decision.Approved ? "approved" : "declined";
                    if (providerKind == CardAuthorizationAudit.LabProvider)
                        evidence = decision.SyntheticEvidence;
                    if (providerKind == CardAuthorizationAudit.ShadyBankProvider)
                        bankEvidence = decision.ShadyBankEvidence;
                }
                else
                    outcome = "provider_invalid_decision";
            }
            catch (OperationCanceledException) when (authorizationDeadline.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Card authorization provider exceeded its configured deadline.");
                outcome = "provider_timeout";
            }
            catch (HttpRequestException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Card authorization provider HTTP request failed.");
                outcome = "provider_http_error";
            }
            catch (InvalidCardProviderResponseException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Card authorization provider returned an invalid decision.");
                outcome = "provider_invalid_decision";
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Provider exceptions can contain protected request data. Log only the type.
                logger.LogWarning("Card authorization provider failed ({ErrorType}).", ex.GetType().Name);
                outcome = "provider_failed";
            }
            finally
            {
                // Observe late faults without retaining or logging provider messages.
                _ = ObserveCardInvocationAsync(invocation);
            }
        }

        var audit = NewCardAttempt(terminalId, Detail(outcome, evidence), evidence);
        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && evidence is { DecisionId: not null, Replayed: false })
        {
            // The quote-derived audit ID is a durable, unique decision key.
            // A reconnect may have committed it while this request was being
            // evaluated. Reuse only the exact matching safe decision.
            db.Entry(audit).State = EntityState.Detached;
            response = decline;
            outcome = "audit_failed";
            try
            {
                var recordedDecision = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(
                    row => row.Id == evidence.DecisionId, stoppingToken);
                if (CardAuthorizationAudit.TryReplay(recordedDecision, terminalId, request.Data[0], evidence, out var replay)
                    && DlogCardAuthorizationWire.TryBuildDecision(protectedRequest.Span, replay.Approved,
                        replay.AuthorizationReference.Span, out var replayResponse))
                {
                    outcome = replay.Approved ? "approved" : "declined";
                    if (await StoreCardAttemptAsync(NewCardAttempt(terminalId, Detail(outcome, replay.SyntheticEvidence),
                        replay.SyntheticEvidence)))
                        response = replayResponse;
                    else
                        outcome = "audit_failed";
                }
                else if (recordedDecision != null)
                {
                    outcome = "declined";
                    await StoreCardAttemptAsync(NewCardAttempt(terminalId,
                        Detail(outcome, evidence with { Reason = "quote_already_used", DecisionId = null })));
                }
            }
            catch (Exception readError) when (readError is not OperationCanceledException)
            {
                logger.LogWarning("Card decision replay lookup failed ({ErrorType}).", readError.GetType().Name);
            }
            if (outcome == "audit_failed")
                logger.LogWarning("Card decision audit was not stored ({ErrorType}); declining.", ex.GetType().Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.Entry(audit).State = EntityState.Detached;
            logger.LogWarning("Card decision audit was not stored ({ErrorType}); declining.", ex.GetType().Name);
            response = decline;
            outcome = "audit_failed";
        }
        await SendDialogueReplyAsync(stream, request, [.. response, .. receipts], $"card authorization {outcome}");

        CardAuthorizationAuditDetail Detail(string result, SyntheticAuthorizationEvidence? lab) => new(
            request.Data[0], result, providerKind, lab?.Reason ?? bankEvidence?.Reason, lab?.CardAccountId, lab?.QuoteId ?? bankEvidence?.QuoteId,
            lab?.QuotedAmountUsd ?? bankEvidence?.QuotedAmountUsd, lab?.CallSequence ?? bankEvidence?.CallSequence, lab?.Replayed == true ? lab.DecisionId : null,
            request.Data.Length, receipts.Length / 3, RateSource: bankEvidence?.RateSource, RatePlanVersionId: bankEvidence?.RatePlanVersionId,
            CaptureRequestId: result == "approved" ? bankEvidence?.CaptureRequestId : null,
            CapturePeriodSeconds: result == "approved" ? bankEvidence?.CapturePeriodSeconds : null);
    }

    private static async Task ObserveCardInvocationAsync(Task<NccCardAuthorizationDecision> invocation)
    {
        try { await invocation; }
        catch (Exception) { /* Outcomes are already recorded; never log late provider content. */ }
    }

    private bool TryImmediateReplyRetry(NccWirePacket request, out NccWirePacket reply)
    {
        reply = _lastDialogueReply!;
        return (request.Control & NccControl.ReTrans) != 0 && _lastDialogueRequest is { } previous
            && reply != null && previous.Data.AsSpan().SequenceEqual(request.Data)
            && previous.TerminalId.AsSpan().SequenceEqual(request.TerminalId)
            && NccControl.PacketId(previous.Control) == NccControl.PacketId(request.Control);
    }

    private AuditEvent NewCardAttempt(Guid terminalId, CardAuthorizationAuditDetail detail,
        SyntheticAuthorizationEvidence? evidence = null)
    {
        var row = new AuditEvent
        {
            Id = evidence is { DecisionId: { } decision, Replayed: false } ? decision : Guid.NewGuid(),
            Category = "ncc", Action = evidence?.Replayed == true ? CardAuthorizationAudit.ReplayAction : CardAuthorizationAudit.DecisionAction,
            Actor = Actor, Resource = _session.Id.ToString(), TerminalId = terminalId,
            CorrelationId = _session.CorrelationId, TerminalSessionId = _session.Id.ToString(),
            DetailJson = CardAuthorizationAudit.Encode(detail)
        };
        db.AuditEvents.Add(row);
        return row;
    }

    private async Task<bool> StoreCardAttemptAsync(AuditEvent row)
    {
        try
        {
            await db.SaveChangesAsync(stoppingToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.Entry(row).State = EntityState.Detached;
            logger.LogWarning("Card authorization observation was not stored ({ErrorType}).", ex.GetType().Name);
            return false;
        }
    }

    private async Task<byte[]> StoreCallDetailsAsync(byte[] data, Terminal terminal, IReadOnlyList<Range>? records = null)
    {
        var replies = new List<byte>();
        try
        {
            var profile = await db.FirmwareVersions.AsNoTracking().Where(version => version.Id == terminal.FirmwareVersionId)
                .Select(version => version.TableProfileId).FirstOrDefaultAsync(stoppingToken);
            var fullCombinedNumber = profile == FirmwareTableProfiles.MultiPay200;
            foreach (var range in records ?? LeadingCallDetails(data))
            {
                var detail = DlogCallDetailWire.Decode(data.AsSpan()[range], fullCombinedNumber, out var fingerprint);
                await TerminalUploads.StoreAsync(db, terminal.Id, _session.Id, detail, fingerprint, stoppingToken);
                await TrySettleSyntheticCallAsync(terminal.Id, fingerprint);
                var receipt = new byte[3];
                DlogCallDetailWire.WriteAcknowledgement(receipt, detail.Sequence);
                replies.AddRange(receipt);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Call detail storage stopped for session {SessionId} ({ErrorType}).",
                _session.Id, ex.GetType().Name);
        }
        return replies.ToArray();
    }

    private static List<Range> LeadingCallDetails(byte[] data)
    {
        var records = new List<Range>();
        for (var offset = 0; offset < data.Length;)
        {
            var length = DlogCallDetailWire.RecordLength(data[offset]);
            if (length == 0 || length > data.Length - offset)
                break;
            records.Add(offset..(offset + length));
            offset += length;
        }
        return records;
    }

    private async Task TrySettleSyntheticCallAsync(Guid terminalId, string fingerprint)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var settlement = scope.ServiceProvider.GetService<SyntheticLabCdrSettlement>();
            if (settlement != null)
                await settlement.TrySettleAsync(terminalId, fingerprint, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The CDR has already been stored. Its receipt is independent of
            // whether the optional synthetic ledger adjustment succeeds.
            logger.LogWarning("Synthetic call settlement not posted ({ErrorType}).", ex.GetType().Name);
        }
    }

    private async Task<IReadOnlyList<CraftReportedMatch>> MatchMaintenanceReportsAsync(byte[] data)
    {
        await using var scope = scopes.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<CraftIdentifierProtector>();
        var matches = new List<CraftReportedMatch>();
        for (var offset = 0; DlogMaintenanceWire.HasRequestRecord(data.AsSpan(offset)); offset += DlogMaintenanceWire.RequestLength)
        {
            if (!protector.Configured) { matches.Add(new("unconfigured")); continue; }
            var fingerprint = FingerprintMaintenanceRecord(protector, data.AsSpan(offset, DlogMaintenanceWire.RequestLength));
            if (fingerprint == null) { matches.Add(new("malformed")); continue; }
            try
            {
                var technician = await db.CraftTechnicians.AsNoTracking().SingleOrDefaultAsync(row => row.Enabled &&
                    row.IdentifierKeyId == protector.KeyId && row.IdentifierFingerprint == fingerprint, stoppingToken);
                matches.Add(technician == null ? new("unmatched") : new("matched", technician.Id, technician.DisplayName, technician.EmployeeReference));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Association failure must not prevent the normal maintenance acknowledgement.
                logger.LogWarning("Craft identifier association unavailable ({ErrorType}).", ex.GetType().Name);
                matches.Add(new("unavailable"));
            }
        }
        return matches;
    }

    private static string? FingerprintMaintenanceRecord(CraftIdentifierProtector protector, ReadOnlySpan<byte> record)
    {
        Span<char> identifier = stackalloc char[5];
        try { return DlogMaintenanceWire.TryReadReportedIdentifier(record, identifier) ? protector.Fingerprint(identifier) : null; }
        finally { identifier.Clear(); }
    }

    private Task<bool> RecordFieldEventAsync(byte[] data, Terminal? terminal, IReadOnlyList<CraftReportedMatch>? matches = null) =>
        RecordFieldRecordsAsync(FieldEventCatalog.SplitUpload(data), terminal, matches);

    private async Task<bool> RecordFieldRecordsAsync(IEnumerable<byte[]> records, Terminal? terminal, IReadOnlyList<CraftReportedMatch>? matches = null)
    {
        try
        {
            var capturedAt = DateTime.UtcNow;
            var i = 0;
            var observedVersion = false;
            foreach (var record in records)
            {
                // Cashbox observations were committed before the link acknowledgement.
                if (DlogCashboxWire.RecordLength(record[0]) > 0)
                    continue;
                var match = record[0] == DlogMaintenanceWire.RequestMessageType && matches != null && i < matches.Count ? matches[i] : null;
                await dlog.IngestAsync(
                    record,
                    DlogDirection.TerminalToHost,
                    terminal?.Id,
                    _session.Id,
                    _session.CorrelationId,
                    record[0],
                    true,
                    $"{_lastReceivedSequence}:{i++}",
                    capturedAt,
                    _session.CorrelationId,
                    stoppingToken, craftMatch: match, observedOnNccLink: terminal != null && _session.TerminalId == terminal.Id);
                observedVersion |= record[0] == FieldEventCatalog.SoftwareVersionMessageType && record.Length == FieldEventCatalog.SoftwareVersionRecordLength;
            }
            if (observedVersion && terminal != null && _session.TerminalId == terminal.Id)
                await TryActivateFirmwareConfigurationAsync(terminal.Id);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Field event was not stored.");
            return false;
        }
    }

    private async Task TryActivateFirmwareConfigurationAsync(Guid terminalId)
    {
        try
        {
            if (!await db.FirmwareConfigurationTransitions.AsNoTracking().AnyAsync(row => row.TerminalId == terminalId && row.AutoActivateOnContact &&
                row.ActivatedAtUtc == null && row.CancelledAtUtc == null, stoppingToken)) return;

            // A separate context isolates rolled-back activation changes from the
            // active NCC session's tracking state and normal acknowledgement path.
            await using var scope = scopes.CreateAsyncScope();
            var transitionDb = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var distribution = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
            if (!transitionDb.Database.IsRelational()) return;
            await using var transaction = await transitionDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, stoppingToken);
            var plan = await transitionDb.FirmwareConfigurationTransitions.SingleOrDefaultAsync(row => row.TerminalId == terminalId && row.AutoActivateOnContact &&
                row.ActivatedAtUtc == null && row.CancelledAtUtc == null, stoppingToken);
            if (plan == null) return;
            await FirmwareConfigurationTransitionService.Activate(transitionDb, distribution, plan, clock.GetUtcNow().UtcDateTime, stoppingToken,
                currentSessionId: _session.Id);
            await transaction.CommitAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing prerequisite leaves the plan staged and never suppresses
            // ordinary alarm, maintenance or upload acknowledgements.
            logger.LogDebug("Automatic configuration activation not applied ({ErrorType}).", ex.GetType().Name);
        }
    }

    private async Task<bool> SendDialogueReplyAsync(NetworkStream stream, NccWirePacket request, byte[] acknowledgement, string dialogue)
    {
        try
        {
            if ((request.Control & NccControl.ReTrans) != 0 && _lastDialogueReply is { } previous
                && previous.Data.AsSpan().SequenceEqual(acknowledgement))
                await RetransmitMessageAsync(stream, previous);
            else
                _lastDialogueReply = await WriteMessageAsync(stream, request.TerminalId, acknowledgement);

            _lastDialogueRequest = _lastInboundMessage;
            logger.LogInformation("NCC {Dialogue} dialogue acknowledged for session {SessionId}.", dialogue, _session.Id);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "NCC {Dialogue} acknowledgement was not sent.", dialogue);
            return false;
        }
    }

    private async Task SendManagementReplyAsync(NetworkStream stream, NccWirePacket request, Guid terminalId,
        byte[] acknowledgement, string dialogue)
    {
        var existingOffer = _callbackOffer;
        if (acknowledgement.Length + DlogCallbackWire.ScheduleLength <= NccConstants.MaxDataSize &&
            await TryOfferCallbackAsync(stream, terminalId, request.TerminalId, CallbackContinuation.ManagementReply, acknowledgement))
        {
            // The callback schedule can be embedded in an alarm receipt. Retain
            // the exact offered message for a terminal retry of that same alarm.
            if (existingOffer == null && _callbackOffer is { } offer && DlogAlarmWire.TryBuildAcknowledgement(request.Data, out _))
            {
                _lastDialogueRequest = _lastInboundMessage;
                _lastDialogueReply = offer.Frame;
            }
            return;
        }

        await SendDialogueReplyAsync(stream, request, acknowledgement, dialogue);
    }

    private async Task<Terminal?> MatchTerminalAsync(string hex, DateTime capturedAtUtc)
    {
        var rows = await db.Terminals.AsNoTracking()
            .Select(t => new { t.Id, t.TerminalIdHex, t.SiteId })
            .ToListAsync(stoppingToken);
        var hit = rows.FirstOrDefault(t => string.Equals(t.TerminalIdHex, hex, StringComparison.OrdinalIgnoreCase));
        if (hit != null)
            return await db.Terminals.AsNoTracking().FirstOrDefaultAsync(t => t.Id == hit.Id, stoppingToken);
        // A reserved replacement is not a general alias. It is eligible only
        // after the dedicated MT21 transfer may have applied, on a separate call.
        if (_session.TerminalId != null)
            return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var fresh = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var activated = await TerminalIdentityCutoverDelivery.ConfirmAsync(fresh, hex, _session.Id,
                capturedAtUtc, stoppingToken);
            return activated is { } id
                ? await db.Terminals.AsNoTracking().SingleAsync(row => row.Id == id, stoppingToken) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Identity cutover confirmation could not be stored ({ErrorType}).", ex.GetType().Name);
            return null;
        }
    }

    private async Task WriteAckAsync(NetworkStream stream, byte requestControl)
    {
        try
        {
            var ack = NccFrameCodec.Encode(new NccWirePacket
            {
                Control = (byte)(NccControl.Ack | NccControl.PacketId(requestControl)),
                TerminalId = new byte[NccConstants.TerminalIdSize]
            });
            await WriteCapturedFrameAsync(stream, ack, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "NCC ack was not sent.");
        }
    }

    private async Task SendTimeSyncAsync(NetworkStream stream, NccWirePacket request)
    {
        try
        {
            if ((request.Control & NccControl.ReTrans) != 0 && _lastTimeSync is { } previous)
            {
                if (_timeSyncAwaitingAck && previous.Data[0] == DlogTimeSyncWire.Response)
                    await RetransmitMessageAsync(stream, previous);
                return;
            }

            _lastTimeSync = await WriteMessageAsync(stream, request.TerminalId, DlogTimeSyncWire.EncodeResponse(DateTime.UtcNow));
            _timeSyncAwaitingAck = true;

            logger.LogInformation("NCC time sync sent for session {SessionId}.", _session.Id);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "NCC time sync was not sent.");
        }
    }

    private async Task HandleTimeSyncControlAsync(NetworkStream stream, byte control)
    {
        if (!_timeSyncAwaitingAck || _lastTimeSync is not { } timeSync
            || (control & NccControl.Clr) != 0 || NccControl.PacketId(control) != timeSync.Control)
            return;

        try
        {
            if ((control & NccControl.Ack) != 0)
            {
                if (timeSync.Data is [EndDataMessageType])
                {
                    _timeSyncAwaitingAck = false;
                    return;
                }

                if (_session.TerminalId is { } terminalId &&
                    await TryOfferCallbackAsync(stream, terminalId, timeSync.TerminalId, CallbackContinuation.TimeSync))
                    return;
                await SendTimeSyncEndAsync(stream, timeSync.TerminalId);
            }
            else if ((control & NccControl.Nack) != 0)
            {
                await RetransmitMessageAsync(stream, timeSync);
                logger.LogInformation("NCC time sync message {MessageType} retransmitted for session {SessionId}.",
                    timeSync.Data[0], _session.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "NCC time sync follow-up was not sent.");
        }
    }

    private async Task SendTimeSyncEndAsync(NetworkStream stream, byte[] terminalId)
    {
        _lastTimeSync = await WriteMessageAsync(stream, terminalId, [EndDataMessageType]);
        logger.LogInformation("NCC time sync end sent for session {SessionId}.", _session.Id);
    }

    private async Task<bool> TryOfferCallbackAsync(NetworkStream stream, Guid terminalId, byte[] wireId,
        CallbackContinuation continuation, byte[]? acknowledgement = null, byte[]? cashboxStatus = null)
    {
        if (_callbackOffer != null)
            return true;
        if (_callbackOffered || _installStarted || _firmwareTables != null)
            return false;

        var request = await db.TerminalCallbackRequests.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == terminalId
            && row.CancelledAtUtc == null && row.ReturnedAtUtc == null, stoppingToken);
        if (request == null)
            return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var due = request.ProgrammedForUtc ?? (request.DueAtUtc > now ? request.DueAtUtc.Value : now);
        due = due.AddTicks(-(due.Ticks % TimeSpan.TicksPerSecond));
        var terminalClock = due.AddMinutes(request.TerminalClockOffsetMinutes);
        if (!DlogCallbackWire.CanEncode(terminalClock))
            return false;

        var schedule = DlogCallbackWire.EncodeSchedule(terminalClock);
        var payload = acknowledgement == null
            ? schedule
            : [.. acknowledgement.AsSpan(..^1), .. schedule, EndDataMessageType];
        var message = await WriteMessageAsync(stream, wireId, payload);
        _callbackOffer = new CallbackOffer(request, due, message, continuation, cashboxStatus);
        _callbackOffered = true;
        return true;
    }

    private async Task HandleCallbackControlAsync(NetworkStream stream, byte control)
    {
        var offer = _callbackOffer!;
        if ((control & NccControl.Clr) != 0 || NccControl.PacketId(control) != offer.Frame.Control)
            return;

        if ((control & NccControl.Nack) != 0)
        {
            if (offer.Retransmissions >= MaxInstallRetransmissions)
                throw new IOException("The terminal rejected the callback request.");
            await RetransmitMessageAsync(stream, offer.Frame);
            offer.Retransmissions++;
            return;
        }
        if ((control & NccControl.Ack) == 0)
            return;

        _callbackOffer = null;
        var row = await db.TerminalCallbackRequests.SingleOrDefaultAsync(candidate => candidate.Id == offer.Request.Id
            && candidate.Version == offer.Request.Version, stoppingToken);
        if (row != null)
        {
            row.ProgrammedForUtc = offer.DueAtUtc;
            row.AcknowledgedAtUtc ??= clock.GetUtcNow().UtcDateTime;
            row.AcknowledgedSessionId = _session.Id;
            await SaveCallbackReceiptAsync(row, "acknowledged");
        }

        if (offer.Continuation == CallbackContinuation.TimeSync)
        {
            await SendTimeSyncEndAsync(stream, offer.Frame.TerminalId);
            return;
        }
        if (offer.Continuation == CallbackContinuation.ManagementReply)
            return;
        var terminal = await db.Terminals.AsNoTracking().SingleOrDefaultAsync(phone => phone.Id == offer.Request.TerminalId, stoppingToken);
        if (terminal != null)
            await StartInstallAsync(stream, offer.Frame.TerminalId, terminal, offer.Continuation == CallbackContinuation.CallIn,
                cashboxStatus: offer.CashboxStatus, mandatoryFull: offer.Continuation == CallbackContinuation.FullInstall,
                allowCutover: offer.Continuation == CallbackContinuation.CallIn,
                allowAutomaticChangedRecords: offer.Continuation == CallbackContinuation.CraftUpdate);
    }

    private async Task<TerminalCallbackRequest?> RecordCallbackReturnAsync(Guid terminalId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.TerminalCallbackRequests.SingleOrDefaultAsync(candidate => candidate.TerminalId == terminalId
            && candidate.AcknowledgedSessionId != null
            && candidate.ProgrammedForUtc <= now && candidate.CancelledAtUtc == null && candidate.ReturnedAtUtc == null, stoppingToken);
        if (row == null)
            return null;

        row.ReturnedAtUtc = now;
        return await SaveCallbackReceiptAsync(row, "returned") ? row : null;
    }

    private async Task<bool> SaveCallbackReceiptAsync(TerminalCallbackRequest row, string action)
    {
        row.Version++;
        var audit = Audit($"callback_{action}", new { callbackId = row.Id, sessionId = _session.Id, row.ProgrammedForUtc }, row.TerminalId);
        try { await db.SaveChangesAsync(stoppingToken); }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(row).State = EntityState.Detached;
            if (audit != null) db.Entry(audit).State = EntityState.Detached;
            return false;
        }
        return true;
    }

    private enum CallbackContinuation { TimeSync, CallIn, Install, FullInstall, ManagementReply, CraftUpdate }

    private sealed record CallbackOffer(TerminalCallbackRequest Request, DateTime DueAtUtc, NccWirePacket Frame,
        CallbackContinuation Continuation, byte[]? CashboxStatus)
    {
        public int Retransmissions { get; set; }
    }

    private Task RetransmitMessageAsync(NetworkStream stream, NccWirePacket message)
    {
        var retry = NccFrameCodec.Encode(new NccWirePacket
        {
            Control = (byte)(message.Control | NccControl.ReTrans),
            TerminalId = message.TerminalId,
            Data = message.Data
        });
        return WriteCapturedFrameAsync(stream, retry, stoppingToken, DlogTimeSyncWire.DecodeResponse(message.Data));
    }

    private async Task<NccWirePacket> WriteMessageAsync(NetworkStream stream, byte[] terminalId, byte[] data)
    {
        var message = new NccWirePacket { Control = _nextMessagePacketId, TerminalId = terminalId, Data = data };
        await WriteCapturedFrameAsync(stream, NccFrameCodec.Encode(message), stoppingToken, DlogTimeSyncWire.DecodeResponse(data));
        _nextMessagePacketId = NccControl.PacketId((byte)(_nextMessagePacketId + 1));
        return message;
    }

    private NccSessionFrame CaptureFrame(byte control, int wireLength, bool sentByHost)
    {
        lock (_captureOrder)
        {
            var now = DateTime.UtcNow;
            return new NccSessionFrame
            {
                NccSessionId = _session.Id,
                Sequence = ++_sequence,
                SentByHost = sentByHost,
                LinkControl = control,
                WireLength = wireLength,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = Actor,
                UpdatedBy = Actor
            };
        }
    }

    private async Task WriteCapturedFrameAsync(NetworkStream stream, ReadOnlyMemory<byte> bytes, CancellationToken ct,
        DateTime? offeredClockUtc = null)
    {
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(ct, stoppingToken);
        writeCts.CancelAfter(settings.ReadTimeout);
        NccSessionFrame frame;
        await _writeGate.WaitAsync(writeCts.Token);
        try
        {
            if (_terminalCleared)
                throw new IOException("The terminal has cleared the NCC dialogue.");
            frame = CaptureFrame(bytes.Span[1], bytes.Length, sentByHost: true);
            await stream.WriteAsync(bytes, writeCts.Token);
        }
        finally
        {
            _writeGate.Release();
        }

        // Craft writes can arrive on a different request. The link's context stays with its reader.
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var traceDb = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            traceDb.NccSessionFrames.Add(frame);
            if (offeredClockUtc is { } clock)
            {
                traceDb.AuditEvents.Add(new AuditEvent
                {
                    Category = "ncc", Action = "clock_sync_offered", Actor = Actor,
                    Resource = _session.Id.ToString(), TerminalId = _session.TerminalId,
                    CorrelationId = _session.CorrelationId, TerminalSessionId = _session.Id.ToString(),
                    DetailJson = ClockOfferObservation.Encode(frame.Sequence, clock,
                        (frame.LinkControl.GetValueOrDefault() & NccControl.ReTrans) != 0)
                });
            }
            await traceDb.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sent NCC frame metadata was not stored.");
        }
    }

    private async Task TryRateAsync(NetworkStream stream, NccWirePacket packet, byte[] receipts)
    {
        if (TryImmediateReplyRetry(packet, out var reply))
        {
            await SendDialogueReplyAsync(stream, packet, reply.Data, "rate request retry");
            return;
        }
        if (!DlogRateWire.TryParseRequest(packet.Data, out var request))
            return;

        Guid? planId = null;
        Guid? publishedVersionId = null;
        Guid? customerId = null;
        string ratePlanSource = "unmatched";
        bool restoreNationalTollPrefix = false;
        if (_session.TerminalId is { } terminalId)
        {
            var resolution = await HostRatePlanResolver.ForTerminalAsync(db, terminalId, stoppingToken);
            customerId = resolution.CustomerId;
            planId = resolution.RatePlanId;
            publishedVersionId = resolution.PublishedVersionId;
            ratePlanSource = resolution.Source;
            restoreNationalTollPrefix = resolution.RestoreNationalTollPrefix;
        }

        var quoteDigits = DlogRateWire.HostQuoteDialedDigits(request, restoreNationalTollPrefix);
        var quote = await RatingWorkflow.QuoteAsync(db, new RatingWorkflow.QuoteRequestDto(
            quoteDigits,
            RatingMode.RealTimeRated,
            planId,
            customerId,
            1m,
            DateTime.UtcNow, publishedVersionId), stoppingToken);
        publishedVersionId = quote.AppliedVersionId;
        var wire = quote.Allowed && quote.Pricing is { Mode: not CallPricingMode.LegacyProrated } pricing
            ? DlogRateWire.QuoteFromPricing(pricing.FirstChargeUsd, pricing.FollowingChargeUsd,
                pricing.Mode == CallPricingMode.FlatCall, request.CallType)
            : DlogRateWire.QuoteFromDecision(quote.Allowed, quote.Blocked, quote.AmountUsd, request.CallType);
        var response = DlogRateWire.EncodeResponse(wire);
        var call = new CallRecord
        {
            TerminalId = _session.TerminalId,
            DialedDigits = request.DialedDigits,
            RequestRaw = packet.Data.AsSpan(0, Math.Min(packet.Data.Length, DlogRateWire.RequestLength)).ToArray(),
            ResponseRaw = response,
            Mode = RatingMode.RealTimeRated,
            Disposition = RatingWorkflow.ToDisposition(quote.DecisionKind),
            Reconciliation = ReconciliationStatus.Pending,
            StartedAtUtc = DateTime.UtcNow,
            AppliedRatePlanVersionId = publishedVersionId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CreatedBy = Actor,
            UpdatedBy = Actor
        };
        call.Results.Add(RatingWorkflow.ToPersistedResult(call.Id, quote, publishedVersionId));
        db.CallRecords.Add(call);
        Audit("rate_response", new
        {
            correlationId = _session.CorrelationId,
            dialedDigits = request.DialedDigits,
            international = request.International,
            decision = quote.DecisionKind.ToString(),
            amountUsd = quote.AmountUsd,
            ratePlanSource,
            ratePlanId = planId,
            ratePlanVersionId = publishedVersionId,
            nationalTollPrefixRestored = quoteDigits != request.DialedDigits,
            rateInfo = wire.RateInfo
        }, _session.TerminalId);
        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rate quote was not stored.");
        }

        await SendDialogueReplyAsync(stream, packet, [.. response, .. receipts], "rate request");
    }

    private async Task StartInstallAsync(NetworkStream stream, byte[] terminalId, Terminal terminal, bool callIn,
        bool deliverAssignedTables = false, byte[]? cashboxStatus = null, bool mandatoryFull = false, bool allowCutover = false,
        bool distinctCallbackDialogue = false, bool allowAutomaticChangedRecords = false)
    {
        if (_installStarted)
        {
            if (callIn && !HasPendingInstall)
                await SendInstallEndAsync(stream, terminalId);
            return;
        }
        if (await FirmwareConfigurationTransitionService.HoldSourceTables(db, terminal.Id, stoppingToken, _session.Id))
        {
            Audit("firmware_transition_holds_source_tables", new { reason = "running controller version requires transition review" }, terminal.Id);
            await db.SaveChangesAsync(stoppingToken);
            await SendInstallEndAsync(stream, terminalId, cashboxStatus);
            return;
        }
        if (await db.DownloadBatches.AnyAsync(batch => batch.TerminalId == terminal.Id &&
            batch.Status == DownloadBatchStatus.Running && batch.NccSessionId != null, stoppingToken))
        {
            await SendInstallEndAsync(stream, terminalId);
            return;
        }

        if (await TryStartTransitionConfigurationAsync(stream, terminalId, terminal.Id, cashboxStatus))
            return;

        if (await TryStartIdentityCutoverAsync(stream, terminalId, terminal, allowCutover, mandatoryFull, cashboxStatus))
            return;

        // A cold install or memory recovery cannot use a previously queued
        // subset or a different scheduled configuration as its starting set.
        if (!mandatoryFull)
        {
            if (await TryStartScheduledBatchAsync(stream, terminalId, terminal, callIn, cashboxStatus))
                return;

            var queued = await db.DownloadBatches.Include(batch => batch.TableSet)
                .Where(batch => batch.TerminalId == terminal.Id && batch.Status == DownloadBatchStatus.Pending && batch.ScheduleId == null)
                .OrderBy(batch => batch.CreatedAtUtc).ThenBy(batch => batch.Id).FirstOrDefaultAsync(stoppingToken);
            if (queued != null)
            {
                await StartQueuedBatchAsync(stream, terminalId, terminal, queued, cashboxStatus);
                return;
            }
        }

        Guid? tableSetId = null;
        if (!callIn)
            tableSetId = await db.TerminalTableAssignments.AsNoTracking()
                .Where(a => a.TerminalId == terminal.Id && a.TableSet!.Status == TableSetStatus.Published)
                .Select(a => (Guid?)a.TableSetId).FirstOrDefaultAsync(stoppingToken);
        else if (deliverAssignedTables)
        {
            var assigned = await db.TerminalTableAssignments.AsNoTracking().Where(a => a.TerminalId == terminal.Id)
                .Select(a => a.TableSet).FirstOrDefaultAsync(stoppingToken);
            if (await IsSetEligibleAsync(assigned, terminal) && await tables.FindCodeOnlyVersionAsync(assigned!.Id, stoppingToken) == null)
                tableSetId = assigned.Id;
        }

        if (tableSetId == null)
        {
            await SendInstallEndAsync(stream, terminalId, cashboxStatus);
            return;
        }

        _installStarted = true;
        DownloadBatch? prepared = null;
        AuditEvent? selectionAudit = null;
        try
        {
            var changes = allowAutomaticChangedRecords && !mandatoryFull && !callIn
                ? await AcknowledgedTableChanges.SelectAsync(db, terminal, tableSetId.Value, _session.Id, stoppingToken)
                : null;
            var (batch, _) = await tables.PrepareDownloadBatchAsync(
                terminal.Id, tableSetId.Value, changes?.DefinitionIds == null ? DownloadScope.Full : DownloadScope.Partial,
                changes?.DefinitionIds,
                stoppingToken,
                (distinctCallbackDialogue ? "ncc-callback-" : "ncc-install-") + _session.Id.ToString("N"),
                nccSessionId: _session.Id);
            prepared = batch;
            if (changes != null)
                selectionAudit = Audit("automatic_download_selection", new { batchId = batch.Id, tableSetId = tableSetId.Value,
                    changes.BaselineId, changes.Reason, scope = batch.Scope.ToString(), itemCount = batch.Items.Count }, terminal.Id);
            await db.SaveChangesAsync(stoppingToken);
            _batch = batch;
            _mandatoryFullInstall = mandatoryFull;
        }
        catch (Exception ex)
        {
            if (selectionAudit != null && db.Entry(selectionAudit).State == EntityState.Added)
                db.Entry(selectionAudit).State = EntityState.Detached;
            if (prepared != null && db.Entry(prepared).State == EntityState.Added)
            {
                foreach (var item in prepared.Items.ToArray())
                    db.Entry(item).State = EntityState.Detached;
                db.Entry(prepared).State = EntityState.Detached;
            }
            logger.LogWarning(ex, "Install batch was not created.");
            await SendInstallEndAsync(stream, terminalId);
            return;
        }

        await SendBatchAsync(stream, terminalId, terminal, _batch, cashboxStatus);
    }

    private async Task<bool> TryStartIdentityCutoverAsync(NetworkStream stream, byte[] terminalId, Terminal terminal,
        bool ordinaryRequest, bool mandatoryFull, byte[]? cashboxStatus)
    {
        TerminalIdentityCutoverDelivery.Claim claim;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var freshTables = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
            claim = await TerminalIdentityCutoverDelivery.ClaimAsync(freshDb, freshTables, terminal.Id, _session.Id,
                ordinaryRequest, mandatoryFull, clock.GetUtcNow().UtcDateTime, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Identity cutover dispatch could not be stored ({ErrorType}).", ex.GetType().Name);
            await SendInstallEndAsync(stream, terminalId, cashboxStatus);
            return true;
        }
        if (!claim.Hold)
            return false;
        if (claim.ItemId is not { } itemId || claim.BatchId is not { } batchId)
        {
            await SendInstallEndAsync(stream, terminalId, cashboxStatus);
            return true;
        }
        _identityCutover = await db.TerminalIdentityCutoverItems.SingleAsync(row => row.Id == itemId, stoppingToken);
        _batch = await db.DownloadBatches.Include(row => row.TableSet).SingleAsync(row => row.Id == batchId, stoppingToken);
        _installStarted = true;
        // The dedicated claim has checked the reviewed old identity, exact
        // replacement record and staged assignment. Use ordinary ACK pacing.
        await SendBatchAsync(stream, terminalId, terminal, _batch, cashboxStatus);
        return true;
    }

    private async Task<bool> TryStartScheduledBatchAsync(NetworkStream stream, byte[] terminalId, Terminal terminal,
        bool callIn, byte[]? cashboxStatus)
    {
        // A call can arrive while background preparation is committing. Re-read and claim its batch if it won.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var schedule = await db.ScheduledTableDownloads.Include(s => s.NccSession).Include(s => s.TableSet)
                .Include(s => s.LastBatch).ThenInclude(b => b!.Items)
                .FirstOrDefaultAsync(s => s.TerminalId == terminal.Id && s.AvailableAtUtc <= now
                    && s.CompletedAtUtc == null && s.CancelledAtUtc == null, stoppingToken);
            if (schedule == null || (!callIn && (schedule.RecordNumbers != null
                || await tables.FindCodeOnlyVersionAsync(schedule.TableSetId, stoppingToken) != null)))
                return false;
            if (schedule.NccSession?.Status == NccSessionStatus.Active)
                break;

            DownloadBatch? batch = null;
            AuditEvent? audit = null;
            try
            {
                try
                {
                    var records = await tables.ReadScheduledRecordsAsync(schedule, terminal, now, stoppingToken);
                    batch = tables.StageScheduledBatch(schedule, records, now);
                    batch.Status = DownloadBatchStatus.Running;
                    batch.NccSessionId = _session.Id;
                    schedule.NccSessionId = _session.Id;
                    schedule.StartedAtUtc = now;
                    schedule.Version++;
                }
                catch (InvalidOperationException ex)
                {
                    TableDistributionService.StageScheduleCancellation(schedule, now, ex.Message);
                    audit = Audit("scheduled_download_ineligible", new { scheduleId = schedule.Id, schedule.TableSetId, reason = ex.Message }, terminal.Id);
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (DbUpdateException ex)
            {
                var changed = batch ?? schedule.LastBatch;
                db.Entry(schedule).State = EntityState.Detached;
                if (audit != null)
                    db.Entry(audit).State = EntityState.Detached;
                if (changed != null)
                {
                    foreach (var item in changed.Items.ToArray())
                        db.Entry(item).State = EntityState.Detached;
                    db.Entry(changed).State = EntityState.Detached;
                }
                logger.LogInformation(ex, "Scheduled download {ScheduleId} could not be claimed for session {SessionId}.", schedule.Id, _session.Id);
                continue;
            }

            if (batch == null)
                return false;
            _installStarted = true;
            _batch = batch;
            _scheduledDownload = schedule;
            await SendBatchAsync(stream, terminalId, terminal, batch, cashboxStatus);
            return true;
        }

        await SendInstallEndAsync(stream, terminalId, cashboxStatus);
        return true;
    }

    private async Task StartQueuedBatchAsync(NetworkStream stream, byte[] terminalId, Terminal terminal, DownloadBatch queued,
        byte[]? cashboxStatus)
    {
        _installStarted = true;
        queued.Status = DownloadBatchStatus.Running;
        queued.NccSessionId = _session.Id;
        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (DbUpdateException ex)
        {
            db.Entry(queued).State = EntityState.Detached;
            logger.LogInformation(ex, "Queued download {BatchId} could not be claimed for session {SessionId}.", queued.Id, _session.Id);
            await SendInstallEndAsync(stream, terminalId);
            return;
        }
        _batch = queued;
        await SendBatchAsync(stream, terminalId, terminal, queued, cashboxStatus, queuedByOperator: true);
    }

    private async Task<bool> TryStartTransitionConfigurationAsync(NetworkStream stream, byte[] terminalId, Guid phoneId, byte[]? cashboxStatus)
    {
        var plan = await db.FirmwareConfigurationTransitions.AsNoTracking().Where(row => row.TerminalId == phoneId && row.ActivatedAtUtc != null &&
            db.DownloadBatches.Any(batch => batch.Id == (row.RecoveredAtUtc == null ? row.TargetBatchId : row.RecoveryBatchId) && batch.Status == DownloadBatchStatus.Pending))
            .OrderByDescending(row => row.ActivatedAtUtc).FirstOrDefaultAsync(stoppingToken);
        if (plan == null) return false;
        var recovery = plan.RecoveredAtUtc != null;
        var batchId = recovery ? plan.RecoveryBatchId : plan.TargetBatchId;
        var setId = recovery ? plan.SourceTableSetId : plan.TargetTableSetId;
        var firmwareId = recovery ? plan.SourceFirmwareVersionId : plan.TargetFirmwareVersionId;
        var expectedVersion = recovery ? (plan.SourceControllerRom, plan.SourceControllerSoftware) : (plan.TargetControllerRom, plan.TargetControllerSoftware);
        var queued = await db.DownloadBatches.Include(batch => batch.TableSet).SingleOrDefaultAsync(batch => batch.Id == batchId &&
            batch.Status == DownloadBatchStatus.Pending, stoppingToken);
        var current = await db.Terminals.AsNoTracking().SingleOrDefaultAsync(row => row.Id == phoneId, stoppingToken);
        var observation = await FirmwareConfigurationTransitionService.LatestObservation(db, phoneId, stoppingToken);
        var matchingAssignment = await db.TerminalTableAssignments.AsNoTracking().AnyAsync(row => row.TerminalId == phoneId &&
            row.TableSetId == setId, stoppingToken);
        if (queued == null || current == null || current.FirmwareVersionId != firmwareId || !matchingAssignment ||
            queued.TerminalId != phoneId || queued.TableSetId != setId || queued.Scope != DownloadScope.Full || queued.ScheduleId != null ||
            observation == null || FieldEventCatalog.ReadControllerVersion(observation.RawPayload) != expectedVersion)
        {
            Audit("firmware_transition_holds_target_tables", new { recovery, reason = "reviewed assignment or observed version needs review" }, phoneId);
            await db.SaveChangesAsync(stoppingToken);
            await SendInstallEndAsync(stream, terminalId, cashboxStatus);
            return true;
        }
        // Claim the exact full batch even for a mandatory INSTALL request. It is
        // not an older partial delivery, and must not be replaced by a fresh batch.
        await StartQueuedBatchAsync(stream, terminalId, current, queued, cashboxStatus);
        return true;
    }

    private async Task SendBatchAsync(NetworkStream stream, byte[] terminalId, Terminal terminal, DownloadBatch batch,
        byte[]? cashboxStatus, bool queuedByOperator = false)
    {
        var items = await db.DownloadBatchItems
            .Include(i => i.TableVersion)!.ThenInclude(v => v!.TableDefinition)
            .Include(i => i.TableVersion)!.ThenInclude(v => v!.TablePayload)
            .Where(i => i.DownloadBatchId == batch.Id)
            .OrderBy(i => i.StepIndex)
            .ToListAsync(stoppingToken);

        var access = items.Select(item => item.TableVersion).OfType<TableVersion>()
            .Where(version => version.TableDefinition?.TableNumber == 21).ToArray();
        if (_identityCutover == null && TableDistributionService.AccessRecordIssue(access, terminal.TerminalIdHex) is { } accessIssue)
        {
            await RejectBatchBeforeSendAsync(stream, terminalId, items, accessIssue, cashboxStatus);
            return;
        }

        if (queuedByOperator)
        {
            var versions = items.Select(item => item.TableVersion).OfType<TableVersion>().ToArray();
            var definitions = versions.Select(version => version.TableDefinitionId).ToArray();
            if (versions.Any(version => version.TableDefinition?.TableNumber == CodeDownloadWire.MessageType) ||
                !await IsSetEligibleAsync(batch.TableSet, terminal, definitions))
            {
                await RejectBatchBeforeSendAsync(stream, terminalId, items,
                    "The queued records no longer match the phone or its configuration.", cashboxStatus);
                return;
            }
        }

        var installedFirmwareId = await db.Terminals.AsNoTracking().Where(phone => phone.Id == terminal.Id)
            .Select(phone => phone.FirmwareVersionId).SingleAsync(stoppingToken);
        var profile = await tables.FindInstalledProfileAsync(installedFirmwareId, stoppingToken);
        if (TableSetFirmwareCompatibility.SelectionIssue(profile,
            TableSetFirmwareCompatibility.Shapes(items.Select(item => item.TableVersion).OfType<TableVersion>())) is { } issue)
        {
            await RejectBatchBeforeSendAsync(stream, terminalId, items, issue, cashboxStatus);
            return;
        }

        await StartFirmwareTableTransferAsync(stream, terminalId, items, cashboxStatus);
    }

    private async Task<bool> IsSetEligibleAsync(TableSet? set, Terminal terminal, Guid[]? partialDefinitions = null)
    {
        if (set?.Status != TableSetStatus.Published ||
            TableSetFirmwareCompatibility.Mismatch(set, terminal.FirmwareVersionId) != null)
            return false;
        if (set.CustomerId is { } customerId &&
            !await db.Sites.AsNoTracking().AnyAsync(s => s.Id == terminal.SiteId && s.CustomerId == customerId, stoppingToken))
            return false;

        var query = db.TableVersions.AsNoTracking().Include(v => v.TablePayload)
            .Where(v => v.TableSetId == set.Id && v.TableDefinition!.TableNumber == 21);
        if (partialDefinitions != null)
            query = query.Where(version => partialDefinitions.Contains(version.TableDefinitionId));
        return TableDistributionService.AccessRecordIssue(await query.Take(2).ToListAsync(stoppingToken), terminal.TerminalIdHex) == null;
    }

    private async Task StartFirmwareTableTransferAsync(NetworkStream stream, byte[] terminalId, List<DownloadBatchItem> items,
        byte[]? cashboxStatus)
    {
        if (items.Any(item => item.TableVersion == null) ||
            !TableDistributionService.HasCompleteDlogRecords(items.Select(item => item.TableVersion!)))
        {
            await RejectBatchBeforeSendAsync(stream, terminalId, items,
                "The download must contain complete DLOG table records with matching message types.", cashboxStatus);
            return;
        }

        var transfer = new FirmwareTableTransfer(terminalId);
        transfer.Frames.Enqueue(new FirmwareTableFrame(cashboxStatus == null
            ? [TableUpdateMessageType] : [TableUpdateMessageType, .. cashboxStatus]));
        foreach (var item in items)
        {
            var payload = TableDistributionService.GetEffectivePayload(item.TableVersion!);
            _waiting.Add(payload[0], item);
            for (var offset = 0; offset < payload.Length; offset += NccConstants.MaxDataSize)
            {
                var end = Math.Min(offset + NccConstants.MaxDataSize, payload.Length);
                transfer.Frames.Enqueue(new FirmwareTableFrame(payload[offset..end], item, end == payload.Length));
            }
        }
        _firmwareTables = transfer;
        await SendNextFirmwareTableFrameAsync(stream, transfer);
    }

    private async Task RejectBatchBeforeSendAsync(NetworkStream stream, byte[] terminalId,
        List<DownloadBatchItem> items, string reason, byte[]? cashboxStatus)
    {
        foreach (var item in items)
        {
            Advance(item, TableDownloadTrigger.FailPermanent);
            item.LastAckStatus = "not_sent";
            item.ErrorDetail = reason;
        }
        await FinishBatchAsync(reason);
        await SendInstallEndAsync(stream, terminalId, cashboxStatus);
    }

    private async Task SendNextFirmwareTableFrameAsync(NetworkStream stream, FirmwareTableTransfer transfer)
    {
        if (transfer.InFlight != null || transfer.WaitingForTableAck)
            return;

        try
        {
            if (!transfer.Frames.TryDequeue(out var frame))
            {
                transfer.InFlight = await WriteMessageAsync(stream, transfer.TerminalId, [EndDataMessageType]);
                transfer.IsEnding = true;
                return;
            }

            if (frame.Item is { HostDownloadPhase: TableDownloadHostPhase.Queued } queued)
            {
                Advance(queued, TableDownloadTrigger.BeginHeaderTransfer);
                Advance(queued, TableDownloadTrigger.HeaderSent);
            }

            if (_identityCutover != null && frame.Item != null && _identityCutover.PossiblyAppliedAtUtc == null)
            {
                // Commit the recovery evidence before attempting the actual
                // record. A disconnected write may have reached the phone.
                _identityCutover.MarkPossiblyApplied(clock.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(stoppingToken);
            }
            transfer.InFlight = await WriteMessageAsync(stream, transfer.TerminalId, frame.Data);
            if (frame.Item is { } sent && frame.EndsTable)
            {
                Advance(sent, TableDownloadTrigger.PayloadSent);
                transfer.WaitingForTableAck = true;
                await db.SaveChangesAsync(stoppingToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "NCC install frame was not sent.");
            await FailOutstandingAsync("install frame was not sent");
        }
    }

    private async Task HandleFirmwareTableControlAsync(NetworkStream stream, byte control)
    {
        if (_firmwareTables is not { } transfer)
            return;
        var inFlight = transfer.InFlight;
        var matchesPending = inFlight != null && NccControl.PacketId(control) == inFlight.Control;
        if (transfer.IsEnding && matchesPending && (control & NccControl.Ack) != 0)
        {
            _firmwareTables = null;
            await FinishBatchAsync(null);
            return;
        }
        if ((control & NccControl.Clr) != 0)
        {
            await FailOutstandingAsync($"terminal cleared the call before install completed (session frame {_lastReceivedSequence})");
            return;
        }
        if (inFlight == null || !matchesPending)
            return;

        if ((control & NccControl.Ack) != 0)
        {
            transfer.InFlight = null;
            transfer.Retransmissions = 0;
            await SendNextFirmwareTableFrameAsync(stream, transfer);
        }
        else if ((control & NccControl.Nack) != 0)
        {
            if (transfer.Retransmissions >= MaxInstallRetransmissions)
            {
                await FailOutstandingAsync($"terminal rejected the install frame (session frame {_lastReceivedSequence})", ackStatus: "link_nak");
                return;
            }

            try
            {
                await RetransmitMessageAsync(stream, inFlight);
                transfer.Retransmissions++;
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "NCC install retransmission was not sent.");
                await FailOutstandingAsync("install retransmission was not sent");
            }
        }
    }

    private static byte[] InstallEndPayload(byte[]? cashboxStatus) => cashboxStatus == null
        ? [EndDataMessageType] : [TableUpdateMessageType, .. cashboxStatus, EndDataMessageType];

    private async Task SendInstallEndAsync(NetworkStream stream, byte[] terminalId, byte[]? cashboxStatus = null)
    {
        try
        {
            await WriteMessageAsync(stream, terminalId, InstallEndPayload(cashboxStatus));
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "NCC install end was not sent.");
        }
    }

    private async Task ApplyTableAckAsync(NetworkStream stream, byte tableType, byte[] rawFrame)
    {
        if (_batch == null)
            return;

        if (tableType is 0 or 0xFF)
        {
            var hex = Convert.ToHexString(rawFrame);
            foreach (var item in _waiting.Values.ToList())
            {
                var notSent = DownloadFailureDiagnostics.WasNotSent(item.HostDownloadPhase);
                if (!notSent)
                    Advance(item, TableDownloadTrigger.NakReceived);
                Advance(item, TableDownloadTrigger.FailPermanent);
                item.Succeeded = false;
                item.LastAckStatus = notSent ? "not_sent" : "nak";
                item.ErrorDetail = notSent ? DownloadFailureDiagnostics.NotSentReason : "negative acknowledgement " + hex;
            }

            _waiting.Clear();
            _firmwareTables = null;
            await FinishBatchAsync("terminal reported a negative acknowledgement " + hex);
            return;
        }

        if (!_waiting.TryGetValue(tableType, out var matched) || matched.HostDownloadPhase != TableDownloadHostPhase.WaitingForAck)
            return;
        _waiting.Remove(tableType);

        Advance(matched, TableDownloadTrigger.AckReceived);
        matched.Succeeded = true;
        matched.LastAckStatus = "ack";
        matched.ErrorDetail = null;
        if (_identityCutover != null && tableType == 21)
            _identityCutover.MarkApplicationAcknowledged(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(stoppingToken);

        if (_firmwareTables is { } transfer)
        {
            transfer.WaitingForTableAck = false;
            await SendNextFirmwareTableFrameAsync(stream, transfer);
        }
        else if (_waiting.Count == 0)
            await FinishBatchAsync(null);
    }

    private async Task FailOutstandingAsync(string reason, bool missingAcknowledgement = false, string ackStatus = "interrupted")
    {
        var transfer = _firmwareTables;
        var active = _waiting.FirstOrDefault(pair => !DownloadFailureDiagnostics.WasNotSent(pair.Value.HostDownloadPhase));
        if (missingAcknowledgement)
            ackStatus = "missing";
        if (missingAcknowledgement && transfer != null)
        {
            if (transfer.IsEnding)
                reason = "End-of-data link acknowledgement was missing.";
            else if (active.Value == null)
                reason = "Start-of-download link acknowledgement was missing.";
            else if (transfer.InFlight != null)
            {
                ackStatus = "missing_link_ack";
                reason = $"Link acknowledgement was missing for MT{active.Key} fragment.";
            }
            else if (transfer.WaitingForTableAck)
            {
                ackStatus = "missing_table_ack";
                reason = $"Table acknowledgement was missing for MT{active.Key} (MT14).";
            }
        }

        foreach (var item in _waiting.Values)
        {
            var notSent = DownloadFailureDiagnostics.WasNotSent(item.HostDownloadPhase);
            Advance(item, TableDownloadTrigger.FailPermanent);
            item.Succeeded = false;
            item.LastAckStatus = notSent ? "not_sent" : ackStatus;
            item.ErrorDetail = notSent ? DownloadFailureDiagnostics.NotSentReason : reason;
        }

        _waiting.Clear();
        _firmwareTables = null;
        await FinishBatchAsync(reason);
    }

    private async Task FinishBatchAsync(string? error)
    {
        if (_batch == null)
            return;
        var anyFailed = _batch.Items.Any(i => i.HostDownloadPhase == TableDownloadHostPhase.Failed);
        if (error == null && !anyFailed)
        {
            _batch.Status = DownloadBatchStatus.Completed;
            _batch.LastError = null;
            _batch.CompletedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _batch.Status = DownloadBatchStatus.Failed;
            _batch.LastError = error ?? "install did not complete";
            _batch.CompletedAtUtc = DateTime.UtcNow;
        }

        _batch.UpdatedAtUtc = DateTime.UtcNow;
        _identityCutover?.EndTransfer(clock.GetUtcNow().UtcDateTime, _batch.LastError);
        try
        {
            CoveredPartialWork? covered = null;
            AuditEvent? cancellationAudit = null;
            var retryCovered = false;
            if (_scheduledDownload is { } schedule)
            {
                schedule.NccSessionId = null;
                schedule.Version++;
                if (_batch.Status == DownloadBatchStatus.Completed)
                {
                    schedule.CompletedAtUtc = _batch.CompletedAtUtc;
                    await tables.StageDeliveredAssignmentAsync(schedule, _batch.CompletedAtUtc.Value, CancellationToken.None);
                }
            }
            if (_mandatoryFullInstall && _batch.Status == DownloadBatchStatus.Completed)
            {
                try
                {
                    covered = await tables.StageCoveredPartialDownloadsAsync(_batch, CancellationToken.None);
                    if (covered.HasChanges)
                        cancellationAudit = Audit("partial_downloads_superseded", CoveredDetail(_batch, covered), _batch.TerminalId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not review redundant partial work during full install completion.");
                    retryCovered = true;
                }
            }
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex) when (covered?.HasChanges == true && (ex is DbUpdateException ||
                ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected }))
            {
                // A worker or operator may have changed older pending work.
                // Preserve the completed full install, then review fresh rows.
                foreach (var batch in covered.Batches)
                {
                    foreach (var item in batch.Items.ToArray())
                        db.Entry(item).State = EntityState.Detached;
                    db.Entry(batch).State = EntityState.Detached;
                }
                foreach (var redundantSchedule in covered.Schedules)
                    db.Entry(redundantSchedule).State = EntityState.Detached;
                if (cancellationAudit != null)
                    db.Entry(cancellationAudit).State = EntityState.Detached;
                await db.SaveChangesAsync(CancellationToken.None);
                retryCovered = true;
            }
            catch (DbUpdateException ex) when (_scheduledDownload != null &&
                (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                && ex.Entries.Count > 0 && ex.Entries.All(entry => entry.Entity is TerminalTableAssignment))
            {
                // A newer operator assignment took precedence. The terminal's confirmed delivery still counts.
                foreach (var entry in ex.Entries)
                    entry.State = EntityState.Detached;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            if (retryCovered)
                await RetryCoveredPartialReviewAsync(_batch.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Install batch status was not stored.");
        }
    }

    private static object CoveredDetail(DownloadBatch installation, CoveredPartialWork covered) => new
    {
        installationBatchId = installation.Id,
        cancelledBatchIds = covered.Batches.Select(batch => batch.Id).ToArray(),
        cancelledScheduleIds = covered.Schedules.Select(schedule => schedule.Id).ToArray()
    };

    private async Task RetryCoveredPartialReviewAsync(Guid installationId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var freshTables = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
            var installation = await freshDb.DownloadBatches.Include(batch => batch.Items)
                .SingleAsync(batch => batch.Id == installationId, CancellationToken.None);
            var covered = await freshTables.StageCoveredPartialDownloadsAsync(installation, CancellationToken.None);
            if (!covered.HasChanges)
                return;
            ApiAudit.Write(freshDb, null, "ncc", "partial_downloads_superseded", _session.Id.ToString(),
                CoveredDetail(installation, covered), installation.TerminalId);
            await freshDb.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A new pickup or operator change has precedence over cleanup.
            logger.LogWarning(ex, "Redundant partial downloads were left for a later eligible call.");
        }
    }

    private static void Advance(DownloadBatchItem item, TableDownloadTrigger trigger)
    {
        if (!TableDownloadStateMachine.TryAdvance(item.HostDownloadPhase, trigger, out var next))
            return;
        item.HostDownloadPhase = next;
        item.ItemStatus = TableDownloadStateMachine.SuggestedItemStatus(next);
        item.UpdatedAtUtc = DateTime.UtcNow;
    }

    private AuditEvent? Audit(string action, object detail, Guid? terminalId)
    {
        var ev = ApiAudit.Write(db, null, "ncc", action, _session.Id.ToString(), detail, terminalId);
        ev.CorrelationId = _session.CorrelationId;
        ev.TerminalSessionId = _session.Id.ToString();
        return ev;
    }

    private sealed record FirmwareTableFrame(byte[] Data, DownloadBatchItem? Item = null, bool EndsTable = false);

    private sealed class FirmwareTableTransfer(byte[] terminalId)
    {
        public byte[] TerminalId { get; } = terminalId;
        public Queue<FirmwareTableFrame> Frames { get; } = new();
        public NccWirePacket? InFlight { get; set; }
        public bool WaitingForTableAck { get; set; }
        public bool IsEnding { get; set; }
        public int Retransmissions { get; set; }
    }
}
