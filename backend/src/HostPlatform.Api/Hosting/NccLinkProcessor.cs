using System.Net.Sockets;
using HostPlatform.Api.Audit;
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
    TimeProvider clock,
    ILogger logger,
    CancellationToken stoppingToken)
{
    private const string Actor = "ncc-listener";
    private const byte EndDataMessageType = 13;
    private const byte TableAckMessageType = 14;
    private const byte TableUpdateMessageType = 18;
    private const byte TableDownloadRequestType = 44;
    private const byte RoutineCallInMessageType = 8;
    private const int MaxInstallRetransmissions = 2;
    private static readonly TimeSpan AckWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FirmwareLinkAckDelay = TimeSpan.FromMilliseconds(350);

    private NccSession _session = null!;
    private int _sequence;
    private byte _nextMessagePacketId;
    private NccWirePacket? _lastInboundMessage;
    private NccWirePacket? _lastTimeSync;
    private NccWirePacket? _lastDialogueReply;
    private NccWirePacket? _lastDialogueRequest;
    private bool _timeSyncAwaitingAck;
    private DownloadBatch? _batch;
    private ScheduledTableDownload? _scheduledDownload;
    private bool _installStarted;
    private bool _firmwareDialogue;
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
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                if (HasPendingInstall || _callbackOffer != null)
                    readCts.CancelAfter(AckWait);

                int n;
                try
                {
                    n = await stream.ReadAsync(buf, readCts.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested && HasPendingInstall)
                {
                    var firmwareTransfer = _firmwareTables != null;
                    await FailOutstandingAsync("acknowledgement was missing");
                    if (firmwareTransfer)
                        break;
                    continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogInformation(ex, "NCC read ended for {Remote}.", remote);
                    break;
                }

                if (n == 0)
                    break;

                IReadOnlyList<NccStreamOrderedItem> items;
                try
                {
                    items = decoder.Feed(buf.AsSpan(0, n));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "NCC decode failed.");
                    continue;
                }

                foreach (var item in items)
                {
                    if (item is not NccStreamParsedFrame parsed || parsed.Frame.IsTruncated)
                        continue;
                    var parse = parsed.Frame.Parse;
                    if (!parse.Success || parse.Packet is not { } packet)
                    {
                        if (parse.Packet is { } retry && IsHeaderOnlyRetransmission(retry, parsed.Frame.RawBytes))
                        {
                            await AcceptFrameAsync(stream, retry, parsed.Frame.RawBytes, headerOnlyRetry: true);
                            logger.LogInformation("NCC header-only retransmission acknowledged for session {SessionId}.", _session.Id);
                        }
                        else
                        {
                            logger.LogInformation("NCC frame rejected for session {SessionId}: {Reason}.",
                                _session.Id, string.Join("; ", parse.Diagnostics));
                        }
                        continue;
                    }

                    await AcceptFrameAsync(stream, packet, parsed.Frame.RawBytes);
                    if ((packet.Control & NccControl.Clr) != 0)
                        return;
                }
            }
        }
        catch (IOException ex)
        {
            logger.LogInformation("NCC link ended for session {SessionId}. {Reason}", _session.Id, ex.Message);
        }
        finally
        {
            if (HasPendingInstall)
                await FailOutstandingAsync("acknowledgement was missing");

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

    private async Task AcceptFrameAsync(NetworkStream stream, NccWirePacket packet, byte[] raw, bool headerOnlyRetry = false)
    {
        var control = NccControl.IsControlPacket(packet.Control);
        var hex = control ? string.Empty : Convert.ToHexString(packet.TerminalId);
        var terminal = control ? null : await MatchTerminalAsync(hex);
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
        var uploadAttention = messageType == DlogCallDetailWire.UploadAttention && packet.Data.Length >= 2;
        var reports = DlogTerminalStatisticsWire.FindReports(packet.Data, out var truncatedReport);
        var statistics = reports.Count > 0 || truncatedReport;
        // A rate request can carry a call record after its fixed-length header.
        var recordedBytes = cardAuthorization || callDetails || statistics || messageType == 6 ||
            (rateRequest && packet.Data.Length > DlogRateWire.RequestLength) ? Array.Empty<byte>() : raw;
        _session.LastFrameSample = recordedBytes;
        _session.UpdatedAtUtc = DateTime.UtcNow;
        _session.UpdatedBy = Actor;

        if (terminal != null)
            outbound.Register(terminal.Id, _session.Id, packet.TerminalId.ToArray(), stream);

        var frame = new NccSessionFrame
        {
            NccSessionId = _session.Id,
            Sequence = ++_sequence,
            TerminalIdHex = hex,
            TerminalMatched = terminal != null,
            RawBytes = recordedBytes,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CreatedBy = Actor,
            UpdatedBy = Actor
        };
        db.NccSessionFrames.Add(frame);
        Audit("frame_stored", new
        {
            correlationId = _session.CorrelationId,
            terminalIdHex = hex,
            matched = terminal != null,
            unknown = !control && terminal == null,
            redactedMessageType = recordedBytes.Length == 0 ? messageType : null
        }, _session.TerminalId);

        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NCC frame was not stored.");
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

        var maintenanceRequest = DlogMaintenanceWire.HasRequestRecord(packet.Data);
        var callIn = packet.Data is [RoutineCallInMessageType] or [DlogCallbackWire.Return];
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
            ? await StoreCallDetailsAsync(packet.Data, terminal) : [];
        if (terminal != null)
        {
            _firmwareDialogue |= maintenanceRequest || rateRequest || cardAuthorization || callIn || callDetails || uploadAttention || statistics
                || packet.Data is [DlogTimeSyncWire.Request] or [TableDownloadRequestType, _, ..];
            // Give the terminal time to arm its reply timer after transmit completion.
            if (_firmwareDialogue)
                await Task.Delay(FirmwareLinkAckDelay, stoppingToken);
        }

        await WriteAckAsync(stream, packet.Control);
        if (headerOnlyRetry)
        {
            if (_lastDialogueReply is { } reply && ReferenceEquals(_lastInboundMessage, _lastDialogueRequest)
                && reply.Data[0] is DlogCallDetailWire.Acknowledgement or DlogCallDetailWire.RequestUpload)
                await SendDialogueReplyAsync(stream, packet, reply.Data, "call upload retry");
            return;
        }

        if (callDetails)
        {
            if (receipts.Length > 0)
                await SendDialogueReplyAsync(stream, packet, receipts, "call detail");
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
            await RecordFieldEventAsync(packet.Data, terminal);
            if (terminal != null && DlogMaintenanceWire.TryBuildAcknowledgement(packet.Data, out var reply))
                await SendManagementReplyAsync(stream, packet, terminal.Id, reply, "maintenance");
            return;
        }

        if (rateRequest)
        {
            await TryRateAsync(stream, packet);
            return;
        }

        if (cardAuthorization)
        {
            // An approval requires a connected authorization provider.
            if (terminal != null && DlogCardAuthorizationWire.TryBuildDecline(packet.Data, out var decline))
                await SendDialogueReplyAsync(stream, packet, decline, "card authorization declined");
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
                await SendDialogueReplyAsync(stream, packet, [DlogCallDetailWire.RequestUpload], "call upload request");
        }

        if (terminal != null && (callIn || applicationType == TableDownloadRequestType && packet.Data.Length >= 2))
        {
            var returned = applicationType == DlogCallbackWire.Return ? await RecordCallbackReturnAsync(terminal.Id) : null;
            var continuation = callIn ? CallbackContinuation.CallIn : CallbackContinuation.Install;
            if (returned == null && await TryOfferCallbackAsync(stream, terminal.Id, packet.TerminalId, continuation))
                return;
            await StartInstallAsync(stream, packet.TerminalId, terminal, callIn, returned?.DeliverAssignedTables == true);
        }
    }

    private async Task<byte[]> StoreCallDetailsAsync(byte[] data, Terminal terminal)
    {
        var replies = new List<byte>();
        try
        {
            var profile = await db.FirmwareVersions.AsNoTracking().Where(version => version.Id == terminal.FirmwareVersionId)
                .Select(version => version.TableProfileId).FirstOrDefaultAsync(stoppingToken);
            var fullCombinedNumber = profile == FirmwareTableProfiles.MultiPay200;
            for (var offset = 0; offset < data.Length;)
            {
                var length = DlogCallDetailWire.RecordLength(data[offset]);
                if (length == 0 || length > data.Length - offset)
                    break;
                var detail = DlogCallDetailWire.Decode(data.AsSpan(offset, length), fullCombinedNumber, out var fingerprint);
                await TerminalUploads.StoreAsync(db, terminal.Id, _session.Id, detail, fingerprint, stoppingToken);
                var receipt = new byte[3];
                DlogCallDetailWire.WriteAcknowledgement(receipt, detail.Sequence);
                replies.AddRange(receipt);
                offset += length;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Call detail storage stopped for session {SessionId} ({ErrorType}).",
                _session.Id, ex.GetType().Name);
        }
        return replies.ToArray();
    }

    private async Task<bool> RecordFieldEventAsync(byte[] data, Terminal? terminal)
    {
        try
        {
            var records = FieldEventCatalog.SplitUpload(data);
            var capturedAt = DateTime.UtcNow;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                await dlog.IngestAsync(
                    record,
                    DlogDirection.TerminalToHost,
                    terminal?.Id,
                    _session.Id,
                    _session.CorrelationId,
                    record[0],
                    true,
                    $"{_sequence}:{i}",
                    capturedAt,
                    _session.CorrelationId,
                    stoppingToken);
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Field event was not stored.");
            return false;
        }
    }

    private async Task SendDialogueReplyAsync(NetworkStream stream, NccWirePacket request, byte[] acknowledgement, string dialogue)
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
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "NCC {Dialogue} acknowledgement was not sent.", dialogue);
        }
    }

    private async Task SendManagementReplyAsync(NetworkStream stream, NccWirePacket request, Guid terminalId,
        byte[] acknowledgement, string dialogue)
    {
        if (acknowledgement.Length + DlogCallbackWire.ScheduleLength <= NccConstants.MaxDataSize &&
            await TryOfferCallbackAsync(stream, terminalId, request.TerminalId, CallbackContinuation.ManagementReply, acknowledgement))
            return;

        await SendDialogueReplyAsync(stream, request, acknowledgement, dialogue);
    }

    private async Task<Terminal?> MatchTerminalAsync(string hex)
    {
        var rows = await db.Terminals.AsNoTracking()
            .Select(t => new { t.Id, t.TerminalIdHex, t.SiteId })
            .ToListAsync(stoppingToken);
        var hit = rows.FirstOrDefault(t => string.Equals(t.TerminalIdHex, hex, StringComparison.OrdinalIgnoreCase));
        if (hit == null)
            return null;
        return await db.Terminals.AsNoTracking().FirstOrDefaultAsync(t => t.Id == hit.Id, stoppingToken);
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
            await stream.WriteAsync(ack, stoppingToken);
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
        CallbackContinuation continuation, byte[]? acknowledgement = null)
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
        _callbackOffer = new CallbackOffer(request, due, message, continuation);
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
            await StartInstallAsync(stream, offer.Frame.TerminalId, terminal, offer.Continuation == CallbackContinuation.CallIn);
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

    private enum CallbackContinuation { TimeSync, CallIn, Install, ManagementReply }

    private sealed record CallbackOffer(TerminalCallbackRequest Request, DateTime DueAtUtc, NccWirePacket Frame, CallbackContinuation Continuation)
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
        return stream.WriteAsync(retry, stoppingToken).AsTask();
    }

    private async Task<NccWirePacket> WriteMessageAsync(NetworkStream stream, byte[] terminalId, byte[] data)
    {
        var message = new NccWirePacket { Control = _nextMessagePacketId, TerminalId = terminalId, Data = data };
        await stream.WriteAsync(NccFrameCodec.Encode(message), stoppingToken);
        _nextMessagePacketId = NccControl.PacketId((byte)(_nextMessagePacketId + 1));
        return message;
    }

    private async Task TryRateAsync(NetworkStream stream, NccWirePacket packet)
    {
        if (!DlogRateWire.TryParseRequest(packet.Data, out var request))
            return;

        Guid? planId = null;
        Guid? publishedVersionId = null;
        Guid? customerId = null;
        if (_session.TerminalId is { } terminalId)
        {
            var siteId = await db.Terminals.AsNoTracking()
                .Where(t => t.Id == terminalId)
                .Select(t => t.SiteId)
                .FirstAsync(stoppingToken);
            customerId = await db.Sites.AsNoTracking()
                .Where(s => s.Id == siteId)
                .Select(s => (Guid?)s.CustomerId)
                .FirstOrDefaultAsync(stoppingToken);
            var plans = await db.RatePlans.AsNoTracking()
                .Where(p => p.CustomerId == customerId && p.PublishedVersionId != null)
                .ToListAsync(stoppingToken);
            var plan = plans
                .OrderByDescending(p => string.Equals(p.Name, "Local default", StringComparison.Ordinal))
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            planId = plan?.Id;
            publishedVersionId = plan?.PublishedVersionId;
        }

        var quote = await RatingWorkflow.QuoteAsync(db, new RatingWorkflow.QuoteRequestDto(
            request.DialedDigits,
            RatingMode.RealTimeRated,
            planId,
            customerId,
            1m,
            DateTime.UtcNow), stoppingToken);
        var wire = DlogRateWire.QuoteFromDecision(quote.Allowed, quote.Blocked, quote.AmountUsd, request.CallType);
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

        try
        {
            await WriteMessageAsync(stream, packet.TerminalId, response);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Rate response was not sent.");
        }
    }

    private async Task StartInstallAsync(NetworkStream stream, byte[] terminalId, Terminal terminal, bool callIn,
        bool deliverAssignedTables = false)
    {
        if (_installStarted)
        {
            if (callIn && !HasPendingInstall)
                await SendInstallEndAsync(stream, terminalId);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var scheduled = await db.ScheduledTableDownloads.Include(s => s.NccSession).Include(s => s.TableSet)
            .FirstOrDefaultAsync(s => s.TerminalId == terminal.Id && s.AvailableAtUtc <= now
                && s.CompletedAtUtc == null && s.CancelledAtUtc == null, stoppingToken);
        var codeVersion = scheduled == null ? null : await tables.FindCodeOnlyVersionAsync(scheduled.TableSetId, stoppingToken);
        if (!callIn && (codeVersion != null || scheduled?.RecordNumbers != null))
        {
            scheduled = null;
            codeVersion = null;
        }
        if (scheduled?.NccSession?.Status == NccSessionStatus.Active)
        {
            await SendInstallEndAsync(stream, terminalId);
            return;
        }

        Guid? tableSetId = null;
        Guid[]? partialDefinitions = null;
        var scheduleIssue = codeVersion == null ? null : CodeDownloadWire.DeliveryIssue(TableDistributionService.GetEffectivePayload(codeVersion), now);
        if (scheduled?.RecordNumbers is { } selected)
        {
            var versions = await db.TableVersions.AsNoTracking().Include(version => version.TableDefinition).Include(version => version.TablePayload)
                .Where(version => version.TableSetId == scheduled.TableSetId).ToArrayAsync(stoppingToken);
            try
            {
                partialDefinitions = TableDistributionService.SelectDataDownloadRecords(versions, selected)
                    .Select(version => version.TableDefinitionId).ToArray();
            }
            catch (InvalidOperationException ex) { scheduleIssue = ex.Message; }
        }
        if (scheduled != null && scheduleIssue == null && await IsSetEligibleAsync(scheduled.TableSet, terminal, partialDefinitions))
            tableSetId = scheduled.TableSetId;
        else
        {
            if (scheduled != null)
            {
                scheduled.CancelledAtUtc = DateTime.UtcNow;
                scheduled.Version++;
                var audit = Audit("scheduled_download_ineligible", new { scheduleId = scheduled.Id, scheduled.TableSetId, reason = scheduleIssue }, terminal.Id);
                try { await db.SaveChangesAsync(stoppingToken); }
                catch (DbUpdateConcurrencyException)
                {
                    db.Entry(scheduled).State = EntityState.Detached;
                    if (audit != null)
                        db.Entry(audit).State = EntityState.Detached;
                    // The schedule changed while eligibility was being checked.
                    await SendInstallEndAsync(stream, terminalId);
                    return;
                }
            }
            scheduled = null;
            partialDefinitions = null;
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
        }

        if (tableSetId == null)
        {
            await SendInstallEndAsync(stream, terminalId);
            return;
        }

        _installStarted = true;
        try
        {
            if (scheduled != null)
            {
                scheduled.NccSessionId = _session.Id;
                scheduled.StartedAtUtc = DateTime.UtcNow;
                scheduled.Version++;
            }
            var (created, _) = await tables.CreateDownloadBatchAsync(
                terminal.Id,
                tableSetId.Value,
                partialDefinitions == null ? DownloadScope.Full : DownloadScope.Partial,
                partialDefinitions,
                stoppingToken,
                "ncc-install-" + _session.Id.ToString("N"),
                scheduled);
            _batch = created;
            _scheduledDownload = scheduled;
        }
        catch (Exception ex)
        {
            if (scheduled != null)
                db.Entry(scheduled).State = EntityState.Detached;
            logger.LogWarning(ex, "Install batch was not created.");
            await SendInstallEndAsync(stream, terminalId);
            return;
        }

        var items = await db.DownloadBatchItems
            .Include(i => i.TableVersion)!.ThenInclude(v => v!.TableDefinition)
            .Include(i => i.TableVersion)!.ThenInclude(v => v!.TablePayload)
            .Where(i => i.DownloadBatchId == _batch.Id)
            .OrderBy(i => i.StepIndex)
            .ToListAsync(stoppingToken);

        await StartFirmwareTableTransferAsync(stream, terminalId, items);
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

    private async Task StartFirmwareTableTransferAsync(NetworkStream stream, byte[] terminalId, List<DownloadBatchItem> items)
    {
        if (items.Any(item => item.TableVersion == null) ||
            !TableDistributionService.HasCompleteDlogRecords(items.Select(item => item.TableVersion!)))
        {
            const string reason = "The assigned set must contain complete DLOG table records with matching message types.";
            foreach (var item in items)
            {
                Advance(item, TableDownloadTrigger.FailPermanent);
                item.Succeeded = false;
                item.LastAckStatus = "not_sent";
                item.ErrorDetail = reason;
            }
            await FinishBatchAsync(reason);
            logger.LogInformation("NCC session {SessionId} has no complete install table set to send.", _session.Id);
            await SendInstallEndAsync(stream, terminalId);
            return;
        }

        var transfer = new FirmwareTableTransfer(terminalId);
        transfer.Frames.Enqueue(new FirmwareTableFrame([TableUpdateMessageType]));
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
            await FailOutstandingAsync($"terminal cleared the call before install completed (session frame {_sequence})");
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
                await FailOutstandingAsync($"terminal rejected the install frame (session frame {_sequence})");
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

    private async Task SendInstallEndAsync(NetworkStream stream, byte[] terminalId)
    {
        try
        {
            await WriteMessageAsync(stream, terminalId, [EndDataMessageType]);
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
                Advance(item, TableDownloadTrigger.NakReceived);
                Advance(item, TableDownloadTrigger.FailPermanent);
                item.Succeeded = false;
                item.LastAckStatus = "nak";
                item.ErrorDetail = "negative acknowledgement " + hex;
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
        await db.SaveChangesAsync(stoppingToken);

        if (_firmwareTables is { } transfer)
        {
            transfer.WaitingForTableAck = false;
            await SendNextFirmwareTableFrameAsync(stream, transfer);
        }
        else if (_waiting.Count == 0)
            await FinishBatchAsync(null);
    }

    private async Task FailOutstandingAsync(string reason)
    {
        foreach (var item in _waiting.Values)
        {
            Advance(item, TableDownloadTrigger.FailPermanent);
            item.Succeeded = false;
            item.LastAckStatus = "missing";
            item.ErrorDetail = reason;
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
        try
        {
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
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Install batch status was not stored.");
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
        ApiAudit.Write(db, null, "ncc", action, _session.Id.ToString(), detail, terminalId);
        var ev = db.ChangeTracker.Entries<AuditEvent>()
            .Select(e => e.Entity)
            .LastOrDefault(e => e.Action == action && e.Resource == _session.Id.ToString());
        if (ev == null)
            return null;
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
