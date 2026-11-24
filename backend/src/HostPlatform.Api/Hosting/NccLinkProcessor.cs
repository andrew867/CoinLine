using System.Net.Sockets;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Rating;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Hosting;

/// <summary>
/// One modem socket. Stores every accepted frame, echoes a control acknowledgement,
/// quotes type 63 against the terminal's published plan, and pushes the assigned install set.
/// A control acknowledgement has no terminal-id field; the session row stores the five bytes.
/// </summary>
internal sealed class NccLinkProcessor(
    HostPlatformDbContext db,
    TableDistributionService tables,
    DlogTransactionEngine dlog,
    INccSessionOutbound outbound,
    ILogger logger,
    CancellationToken stoppingToken)
{
    private const string Actor = "ncc-listener";
    private const byte EndDataMessageType = 13;
    private const byte TableAckMessageType = 14;
    private const byte TableUpdateMessageType = 18;
    private const byte TableDownloadRequestType = 44;
    private const int MaxInstallRetransmissions = 2;
    private static readonly TimeSpan AckWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FirmwareLinkAckDelay = TimeSpan.FromMilliseconds(350);

    private NccSession _session = null!;
    private int _sequence;
    private byte _nextMessagePacketId;
    private NccWirePacket? _lastInboundMessage;
    private NccWirePacket? _lastTimeSync;
    private NccWirePacket? _lastDialogueReply;
    private bool _timeSyncAwaitingAck;
    private DownloadBatch? _batch;
    private bool _installStarted;
    private bool _firmwareDialogue;
    private readonly Dictionary<int, DownloadBatchItem> _waiting = new();
    private FirmwareTableTransfer? _firmwareTables;

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
                if (HasPendingInstall)
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
        _session.LastFrameSample = raw;
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
            RawBytes = raw,
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
            unknown = !control && terminal == null
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
            if (_firmwareTables != null)
                await HandleFirmwareTableControlAsync(stream, packet.Control);
            else
                await HandleTimeSyncControlAsync(stream, packet.Control);
            return;
        }

        var maintenanceRequest = DlogMaintenanceWire.HasRequestRecord(packet.Data);
        var rateRequest = packet.Data is [DlogRateWire.MessageTypeRequest, ..];
        if (terminal != null)
        {
            _firmwareDialogue |= maintenanceRequest || rateRequest
                || packet.Data is [DlogTimeSyncWire.Request] or [TableDownloadRequestType, _, ..];
            // Give the terminal time to arm its reply timer after transmit completion.
            if (_firmwareDialogue)
                await Task.Delay(FirmwareLinkAckDelay, stoppingToken);
        }

        await WriteAckAsync(stream, packet.Control);
        if (headerOnlyRetry)
            return;

        if (packet.Data is [DlogTimeSyncWire.Request])
        {
            if (terminal != null)
                await SendTimeSyncAsync(stream, packet);
            return;
        }

        if (maintenanceRequest)
        {
            if (terminal != null && DlogMaintenanceWire.TryBuildAcknowledgement(packet.Data, out var reply))
                await SendDialogueReplyAsync(stream, packet, reply, "maintenance");
            return;
        }

        if (rateRequest)
        {
            await TryRateAsync(stream, packet);
            return;
        }

        var messageType = packet.Data[0];
        var fieldEvent = FieldEventCatalog.TryDescribe(messageType, out _);
        if (packet.Data.Length >= 2 && messageType == TableAckMessageType)
            await ApplyTableAckAsync(stream, packet.Data[1], raw);
        else if (fieldEvent)
        {
            var stored = await RecordFieldEventAsync(packet, terminal);
            if (stored && terminal != null && messageType == DlogAlarmWire.AlarmMessageType
                && DlogAlarmWire.TryBuildAcknowledgement(packet.Data, out var acknowledgement))
                await SendDialogueReplyAsync(stream, packet, acknowledgement, "alarm");
        }

        if (terminal == null)
            return;

        if (messageType == TableDownloadRequestType && packet.Data.Length >= 2)
            await StartInstallAsync(stream, packet.TerminalId, firmwareRequest: true);
        else if (!fieldEvent && _lastTimeSync == null)
            await StartInstallAsync(stream, packet.TerminalId);
    }

    private async Task<bool> RecordFieldEventAsync(NccWirePacket packet, Terminal? terminal)
    {
        try
        {
            await dlog.IngestAsync(
                packet.Data.ToArray(),
                DlogDirection.TerminalToHost,
                terminal?.Id,
                _session.Id,
                _session.CorrelationId,
                packet.Data[0],
                true,
                _sequence.ToString(),
                DateTime.UtcNow,
                _session.CorrelationId,
                stoppingToken);
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

            logger.LogInformation("NCC {Dialogue} dialogue acknowledged for session {SessionId}.", dialogue, _session.Id);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "NCC {Dialogue} acknowledgement was not sent.", dialogue);
        }
    }

    private async Task<Terminal?> MatchTerminalAsync(string hex)
    {
        var rows = await db.Terminals.AsNoTracking()
            .Select(t => new { t.Id, t.TerminalIdHex, t.SiteId })
            .ToListAsync(stoppingToken);
        var hit = rows.FirstOrDefault(t => string.Equals(t.TerminalIdHex, hex, StringComparison.OrdinalIgnoreCase));
        if (hit == null)
            return null;
        return await db.Terminals.FirstAsync(t => t.Id == hit.Id, stoppingToken);
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

                _lastTimeSync = await WriteMessageAsync(stream, timeSync.TerminalId, [EndDataMessageType]);
                logger.LogInformation("NCC time sync end sent for session {SessionId}.", _session.Id);
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
            RequestRaw = packet.Data.ToArray(),
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

    private async Task StartInstallAsync(NetworkStream stream, byte[] terminalId, bool firmwareRequest = false)
    {
        if (_installStarted || _session.TerminalId is not { } boundId)
            return;
        _installStarted = true;

        var assignment = await db.TerminalTableAssignments.AsNoTracking()
            .Where(a => a.TerminalId == boundId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .FirstOrDefaultAsync(stoppingToken);
        if (assignment == null)
        {
            if (firmwareRequest)
                await SendInstallEndAsync(stream, terminalId);
            return;
        }

        var published = await db.TableSets.AsNoTracking()
            .AnyAsync(s => s.Id == assignment.TableSetId && s.Status == TableSetStatus.Published, stoppingToken);
        if (!published)
        {
            if (firmwareRequest)
                await SendInstallEndAsync(stream, terminalId);
            return;
        }

        try
        {
            var (created, _) = await tables.CreateDownloadBatchAsync(
                boundId,
                assignment.TableSetId,
                DownloadScope.Full,
                null,
                stoppingToken,
                "ncc-install-" + _session.Id.ToString("N"));
            _batch = await db.DownloadBatches.Include(b => b.Items)
                .FirstAsync(b => b.Id == created.Id, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Install batch was not created.");
            return;
        }

        var items = await db.DownloadBatchItems
            .Include(i => i.TableVersion)!.ThenInclude(v => v!.TableDefinition)
            .Include(i => i.TableVersion)!.ThenInclude(v => v!.TablePayload)
            .Where(i => i.DownloadBatchId == _batch.Id)
            .OrderBy(i => i.StepIndex)
            .ToListAsync(stoppingToken);

        if (firmwareRequest)
        {
            await StartFirmwareTableTransferAsync(stream, terminalId, items);
            return;
        }

        foreach (var item in items)
        {
            var version = item.TableVersion;
            var payload = version == null ? Array.Empty<byte>() : TableDistributionService.GetEffectivePayload(version);
            if (payload.Length == 0 || version?.TableDefinition == null)
            {
                Advance(item, TableDownloadTrigger.FailPermanent);
                item.Succeeded = false;
                item.ErrorDetail = "version bytes were empty";
                item.LastAckStatus = "missing";
                continue;
            }

            Advance(item, TableDownloadTrigger.BeginHeaderTransfer);
            Advance(item, TableDownloadTrigger.HeaderSent);
            Advance(item, TableDownloadTrigger.PayloadSent);
            item.LastAckStatus = "pending_terminal_ack";
            _waiting[version.TableDefinition.TableNumber] = item;

            try
            {
                await WriteMessageAsync(stream, terminalId, payload);
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "Install frame was not sent.");
                Advance(item, TableDownloadTrigger.FailPermanent);
                item.ErrorDetail = "frame was not written";
                item.Succeeded = false;
                _waiting.Remove(version.TableDefinition.TableNumber);
            }
        }

        await db.SaveChangesAsync(stoppingToken);
        if (_waiting.Count == 0)
            await FinishBatchAsync("version bytes were empty");
    }

    private async Task StartFirmwareTableTransferAsync(NetworkStream stream, byte[] terminalId, List<DownloadBatchItem> items)
    {
        var records = items.Select(item => (
            Item: item,
            TableNumber: item.TableVersion?.TableDefinition?.TableNumber,
            Payload: item.TableVersion is { } version ? TableDistributionService.GetEffectivePayload(version) : Array.Empty<byte>())).ToArray();
        if (records.Length == 0 || records.Any(record => record.Payload.Length <= 2
            || record.TableNumber != record.Payload[0])
            || records.Select(record => record.TableNumber).Distinct().Count() != records.Length)
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
        foreach (var record in records)
        {
            _waiting.Add(record.Payload[0], record.Item);
            for (var offset = 0; offset < record.Payload.Length; offset += NccConstants.MaxDataSize)
            {
                var end = Math.Min(offset + NccConstants.MaxDataSize, record.Payload.Length);
                transfer.Frames.Enqueue(new FirmwareTableFrame(record.Payload[offset..end], record.Item, end == record.Payload.Length));
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
            await db.SaveChangesAsync(CancellationToken.None);
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

    private void Audit(string action, object detail, Guid? terminalId)
    {
        ApiAudit.Write(db, null, "ncc", action, _session.Id.ToString(), detail, terminalId);
        var ev = db.ChangeTracker.Entries<AuditEvent>()
            .Select(e => e.Entity)
            .LastOrDefault(e => e.Action == action && e.Resource == _session.Id.ToString());
        if (ev == null)
            return;
        ev.CorrelationId = _session.CorrelationId;
        ev.TerminalSessionId = _session.Id.ToString();
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
