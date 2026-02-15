using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Infrastructure.Parsing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class QueuePollerService : BackgroundService
{
    private readonly IDataHubClient _client;
    private readonly ICimParser _parser;
    private readonly IMeteringDataRepository _meteringRepo;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IProcessRepository _processRepo;
    private readonly ISignupRepository _signupRepo;
    private readonly IOnboardingService _onboardingService;
    private readonly ITariffRepository _tariffRepo;
    private readonly IBrsRequestBuilder _brsBuilder;
    private readonly IMessageRepository _messageRepo;
    private readonly IClock _clock;
    private readonly IMessageLog _messageLog;
    private readonly IInvoiceService _invoiceService;
    private readonly EffectuationService _effectuationService;
    private readonly SettlementMetrics _metrics;
    private readonly ILogger<QueuePollerService> _logger;
    private readonly TimeSpan _pollInterval;

    private static readonly QueueName[] AllQueues =
    {
        QueueName.Timeseries,
        QueueName.MasterData,
        QueueName.Charges,
        QueueName.Aggregations,
    };

    public QueuePollerService(
        IDataHubClient client,
        ICimParser parser,
        IMeteringDataRepository meteringRepo,
        IPortfolioRepository portfolioRepo,
        IProcessRepository processRepo,
        ISignupRepository signupRepo,
        IOnboardingService onboardingService,
        ITariffRepository tariffRepo,
        IBrsRequestBuilder brsBuilder,
        IMessageRepository messageRepo,
        IClock clock,
        IMessageLog messageLog,
        IInvoiceService invoiceService,
        EffectuationService effectuationService,
        ILogger<QueuePollerService> logger,
        SettlementMetrics? metrics = null,
        TimeSpan? pollInterval = null)
    {
        _client = client;
        _parser = parser;
        _meteringRepo = meteringRepo;
        _portfolioRepo = portfolioRepo;
        _processRepo = processRepo;
        _signupRepo = signupRepo;
        _onboardingService = onboardingService;
        _tariffRepo = tariffRepo;
        _brsBuilder = brsBuilder;
        _messageRepo = messageRepo;
        _clock = clock;
        _messageLog = messageLog;
        _invoiceService = invoiceService;
        _effectuationService = effectuationService;
        _metrics = metrics ?? new SettlementMetrics();
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue poller starting — polling {QueueCount} queues", AllQueues.Length);

        while (!stoppingToken.IsCancellationRequested)
        {
            var anyProcessed = false;

            foreach (var queue in AllQueues)
            {
                var processed = await PollQueueAsync(queue, stoppingToken);
                if (processed) anyProcessed = true;
            }

            if (!anyProcessed)
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
    }

    public async Task<bool> PollQueueAsync(QueueName queue, CancellationToken ct)
    {
        var message = await _client.PeekAsync(queue, ct);
        if (message is null)
            return false;

        // Structured log scope with correlation ID for end-to-end tracing
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = message.MessageId,
            ["MessageType"] = message.MessageType,
            ["CorrelationId"] = message.CorrelationId,
            ["Queue"] = queue.ToString(),
        });

        _logger.LogInformation("Processing message {MessageId} ({MessageType}) from {Queue}",
            message.MessageId, message.MessageType, queue);

        // Record inbound
        await _messageLog.RecordInboundAsync(
            message.MessageId, message.MessageType, message.CorrelationId,
            queue.ToString(), message.RawPayload.Length, message.RawPayload, ct);

        // Atomic idempotency: claim the message via INSERT ON CONFLICT DO NOTHING.
        // Only the poller that successfully inserts gets to process. No check-then-act race.
        if (!await _messageLog.TryClaimForProcessingAsync(message.MessageId, ct))
        {
            _logger.LogInformation("Message {MessageId} already claimed by another poller, skipping", message.MessageId);
            await _client.DequeueAsync(message.MessageId, ct);
            return true;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await ProcessMessageAsync(message, queue, ct);

            sw.Stop();
            _metrics.RecordMessageProcessed(message.MessageType, queue.ToString());
            _metrics.RecordMessageDuration(sw.Elapsed.TotalMilliseconds, message.MessageType);

            // Message already claimed above — just update status
            await _messageLog.MarkInboundStatusAsync(message.MessageId, "processed", null, ct);
            await _client.DequeueAsync(message.MessageId, ct);

            _logger.LogInformation("Message {MessageId} processed successfully in {DurationMs}ms",
                message.MessageId, sw.Elapsed.TotalMilliseconds);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            // Parse/format errors → permanent failure → dead-letter + dequeue
            _metrics.RecordMessageDeadLettered(message.MessageType, queue.ToString());
            _logger.LogWarning(ex, "Message {MessageId} failed to parse, dead-lettering", message.MessageId);
            await _messageLog.DeadLetterAsync(message.MessageId, queue.ToString(), ex.Message, message.RawPayload, ct);
            await _messageLog.MarkInboundStatusAsync(message.MessageId, "dead_lettered", ex.Message, ct);
            await _client.DequeueAsync(message.MessageId, ct);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            // State machine transition conflicts — leave message on queue for retry.
            _metrics.RecordMessageFailed(message.MessageType, queue.ToString());
            _logger.LogWarning(ex,
                "Message {MessageId} hit a state transition conflict, will retry on next poll",
                message.MessageId);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unexpected errors (DB failures, etc.) — dead-letter to prevent infinite retry
            _metrics.RecordMessageDeadLettered(message.MessageType, queue.ToString());
            _logger.LogError(ex, "Message {MessageId} failed with unexpected error, dead-lettering", message.MessageId);
            await _messageLog.DeadLetterAsync(message.MessageId, queue.ToString(), ex.Message, message.RawPayload, ct);
            await _messageLog.MarkInboundStatusAsync(message.MessageId, "dead_lettered", ex.Message, ct);
            await _client.DequeueAsync(message.MessageId, ct);
            return true;
        }
    }

    public Task ReprocessMessageAsync(DataHubMessage message, QueueName queue, CancellationToken ct)
    {
        return ProcessMessageAsync(message, queue, ct);
    }

    private Task ProcessMessageAsync(DataHubMessage message, QueueName queue, CancellationToken ct)
    {
        return queue switch
        {
            QueueName.Timeseries => ProcessTimeseriesAsync(message, ct),
            QueueName.MasterData => ProcessMasterDataAsync(message, ct),
            QueueName.Charges => ProcessChargesAsync(message, ct),
            QueueName.Aggregations => ProcessAggregationsAsync(message, ct),
            _ => Task.CompletedTask,
        };
    }

    private async Task ProcessTimeseriesAsync(DataHubMessage message, CancellationToken ct)
    {
        var seriesList = _parser.ParseRsm012(message.RawPayload);

        foreach (var series in seriesList)
        {
            var regTimestamp = series.RegistrationTimestamp.UtcDateTime;

            // Filter out points with negative quantity (BRS-021: only positive values allowed)
            var validPoints = new List<MeteringDataRow>();
            foreach (var p in series.Points)
            {
                if (p.QuantityKwh < 0)
                {
                    _logger.LogWarning(
                        "Skipping negative quantity {Quantity} kWh at position {Position} for {Gsrn}",
                        p.QuantityKwh, p.Position, series.MeteringPointId);
                    continue;
                }

                validPoints.Add(new MeteringDataRow(
                    p.Timestamp.UtcDateTime, series.Resolution, p.QuantityKwh, p.QualityCode,
                    series.TransactionId, regTimestamp));
            }

            if (validPoints.Count == 0)
                continue;

            var changedCount = await _meteringRepo.StoreTimeSeriesWithHistoryAsync(series.MeteringPointId, validPoints, ct);

            if (changedCount > 0)
            {
                _logger.LogInformation(
                    "Detected {ChangedCount} corrected readings for {Gsrn} — correction settlement may be needed",
                    changedCount, series.MeteringPointId);
            }
        }
    }

    private async Task ProcessMasterDataAsync(DataHubMessage message, CancellationToken ct)
    {
        if (message.MessageType is "RSM-022" or "rsm-022" or "RSM022")
        {
            var masterData = _parser.ParseRsm022(message.RawPayload);

            await _portfolioRepo.EnsureGridAreaAsync(
                masterData.GridAreaCode, masterData.GridOperatorGln,
                $"Grid {masterData.GridAreaCode}", masterData.PriceArea, ct);

            var mp = new MeteringPoint(
                masterData.MeteringPointId, masterData.Type, masterData.SettlementMethod,
                masterData.GridAreaCode, masterData.GridOperatorGln, masterData.PriceArea, "connected");

            try
            {
                await _portfolioRepo.CreateMeteringPointAsync(mp, ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Npgsql.PostgresException)
            {
                _logger.LogInformation("Metering point {Gsrn} already exists, skipping create", masterData.MeteringPointId);
            }

            await _portfolioRepo.ActivateMeteringPointAsync(
                masterData.MeteringPointId, masterData.SupplyStart.UtcDateTime, ct);

            // RSM-022 is the authoritative activation signal from DataHub
            // This is the ONLY place where processes are marked "completed"
            var signup = await _signupRepo.GetActiveByGsrnAsync(masterData.MeteringPointId, ct);
            if (signup is not null && signup.ProcessRequestId.HasValue)
            {
                var process = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
                if (process is not null && process.Status is "cancellation_pending" or "cancelled")
                {
                    _logger.LogInformation(
                        "RSM-022: Skipping activation for process {ProcessId} — process is {Status}",
                        process.Id, process.Status);
                }
                else
                {
                    if (process is not null && !process.CustomerDataReceived)
                        _logger.LogWarning("RSM-022: Customer data (RSM-028) not yet received for process {ProcessId}", process.Id);
                    if (process is not null && !process.TariffDataReceived)
                        _logger.LogWarning("RSM-022: Tariff data (RSM-031) not yet received for process {ProcessId}", process.Id);

                    var effectiveDate = DateOnly.FromDateTime(masterData.SupplyStart.UtcDateTime);

                    // Delegate to EffectuationService which wraps all DB operations in transactions
                    await _effectuationService.ActivateAsync(
                        signup.ProcessRequestId.Value,
                        signup.Id,
                        masterData.MeteringPointId,
                        effectiveDate,
                        process?.ProcessType,
                        process?.DatahubCorrelationId,
                        ct);
                }
            }
            else if (signup is null)
            {
                // No signup — create supply period without contract (pre-onboarding metering points)
                await _portfolioRepo.CreateSupplyPeriodAsync(
                    masterData.MeteringPointId, DateOnly.FromDateTime(masterData.SupplyStart.UtcDateTime), ct);

                _logger.LogInformation("RSM-022: Activated metering point {Gsrn}, supply from {Start} (no signup)",
                    masterData.MeteringPointId, masterData.SupplyStart);
            }
            else
            {
                _logger.LogWarning(
                    "RSM-022: Signup {SignupNumber} for GSRN {Gsrn} has no process request — portfolio not created",
                    signup.SignupNumber, masterData.MeteringPointId);
            }
        }
        else if (message.MessageType is "RSM-001" or "rsm-001" or "RSM001")
        {
            var receipt = _parser.ParseRsm001Response(message.RawPayload);

            var process = await _processRepo.GetByCorrelationIdAsync(receipt.CorrelationId, ct);
            if (process is null)
            {
                _logger.LogWarning("RSM-001: No process found for correlation {CorrelationId}", receipt.CorrelationId);
                return;
            }

            var stateMachine = new ProcessStateMachine(_processRepo, _clock);

            // Status-based disambiguation: same correlation ID is used for both original and cancel RSM-001 response
            if (process.Status == "cancellation_pending")
            {
                if (receipt.Accepted)
                {
                    await stateMachine.MarkCancelledAsync(process.Id, "Cancellation acknowledged by DataHub", ct);
                    await _onboardingService.SyncFromProcessAsync(process.Id, "cancelled", null, ct);
                    _logger.LogInformation("RSM-001: Cancellation acknowledged for process {ProcessId}", process.Id);
                }
                else
                {
                    var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Cancellation rejected";
                    await stateMachine.RevertCancellationAsync(process.Id, reason, ct);
                    await _onboardingService.SyncFromProcessAsync(process.Id, "effectuation_pending", null, ct);
                    _logger.LogWarning("RSM-001: Cancellation rejected for process {ProcessId}: {Reason}", process.Id, reason);
                }
            }
            else if (receipt.Accepted)
            {
                await stateMachine.MarkAcknowledgedAsync(process.Id, ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "effectuation_pending", null, ct);

                _logger.LogInformation("RSM-001: Process {ProcessId} accepted for GSRN {Gsrn}",
                    process.Id, process.Gsrn);
            }
            else
            {
                var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Unknown rejection";
                await stateMachine.MarkRejectedAsync(process.Id, reason, ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "rejected", reason, ct);

                _logger.LogWarning("RSM-001: Process {ProcessId} rejected for GSRN {Gsrn}: {Reason}",
                    process.Id, process.Gsrn, reason);
            }
        }
        else if (message.MessageType is "RSM-005" or "rsm-005" or "RSM005")
        {
            var receipt = _parser.ParseRsm005Response(message.RawPayload);

            var process = await _processRepo.GetByCorrelationIdAsync(receipt.CorrelationId, ct);
            if (process is null)
            {
                _logger.LogWarning("RSM-005: No process found for correlation {CorrelationId}", receipt.CorrelationId);
                return;
            }

            var stateMachine = new ProcessStateMachine(_processRepo, _clock);

            if (receipt.Accepted)
            {
                // BRS-002/010: sent_to_datahub → acknowledged → effectuation_pending → completed
                if (process.Status == "sent_to_datahub")
                    await stateMachine.MarkAcknowledgedAsync(process.Id, ct);

                await stateMachine.MarkCompletedAsync(process.Id, ct);

                // End supply period + contract on effective date
                if (process.EffectiveDate.HasValue)
                {
                    await _portfolioRepo.EndSupplyPeriodAsync(process.Gsrn, process.EffectiveDate.Value, process.ProcessType, ct);
                    await _portfolioRepo.EndContractAsync(process.Gsrn, process.EffectiveDate.Value, ct);
                }

                await _onboardingService.SyncFromProcessAsync(process.Id, "completed", null, ct);
                _logger.LogInformation("RSM-005: Process {ProcessId} accepted — supply ended for {Gsrn}", process.Id, process.Gsrn);
            }
            else
            {
                var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Unknown rejection";
                await stateMachine.MarkRejectedAsync(process.Id, reason, ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "rejected", reason, ct);
                _logger.LogWarning("RSM-005: Process {ProcessId} rejected for {Gsrn}: {Reason}", process.Id, process.Gsrn, reason);
            }
        }
        else if (message.MessageType is "RSM-024" or "rsm-024" or "RSM024")
        {
            // Defensive RSM-024 handler — DataHub may send RSM-024 type for cancel responses
            var receipt = _parser.ParseRsm024Response(message.RawPayload);

            var process = await _processRepo.GetByCorrelationIdAsync(receipt.CorrelationId, ct);
            if (process is null)
            {
                _logger.LogWarning("RSM-024: No process found for correlation {CorrelationId}", receipt.CorrelationId);
                return;
            }

            var stateMachine = new ProcessStateMachine(_processRepo, _clock);

            if (receipt.Accepted)
            {
                await stateMachine.MarkCancelledAsync(process.Id, "Cancellation acknowledged by DataHub (RSM-024)", ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "cancelled", null, ct);
                _logger.LogInformation("RSM-024: Cancellation acknowledged for process {ProcessId}", process.Id);
            }
            else
            {
                var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Cancellation rejected";
                await stateMachine.RevertCancellationAsync(process.Id, reason, ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "effectuation_pending", null, ct);
                _logger.LogWarning("RSM-024: Cancellation rejected for process {ProcessId}: {Reason}", process.Id, reason);
            }
        }
        else if (message.MessageType is "RSM-028" or "rsm-028" or "RSM028")
        {
            var customerData = _parser.ParseRsm028(message.RawPayload);

            await _portfolioRepo.StageCustomerDataAsync(
                customerData.MeteringPointId, customerData.CustomerName, customerData.CprCvr,
                customerData.CustomerType, customerData.Phone, customerData.Email,
                message.CorrelationId, ct);

            if (message.CorrelationId is not null)
                await _processRepo.MarkCustomerDataReceivedAsync(message.CorrelationId, ct);

            _logger.LogInformation(
                "RSM-028: Staged customer data for {Gsrn} — {CustomerName} ({CustomerType})",
                customerData.MeteringPointId, customerData.CustomerName, customerData.CustomerType);
        }
        else if (message.MessageType is "RSM-031" or "rsm-031" or "RSM031")
        {
            var priceData = _parser.ParseRsm031(message.RawPayload);

            await _tariffRepo.StoreTariffAttachmentsAsync(
                priceData.MeteringPointId, priceData.Tariffs, message.CorrelationId, ct);

            if (message.CorrelationId is not null)
                await _processRepo.MarkTariffDataReceivedAsync(message.CorrelationId, ct);

            _logger.LogInformation(
                "RSM-031: Stored {TariffCount} tariff attachments for {Gsrn}",
                priceData.Tariffs.Count, priceData.MeteringPointId);
        }
        else if (message.MessageType is "RSM-004" or "rsm-004" or "RSM004")
        {
            var change = _parser.ParseRsm004(message.RawPayload);

            // Handle reason codes for D11 (auto-cancel), D46 (special rules), E03 (stop of supply)
            if (change.ReasonCode == "D11")
            {
                // Auto-cancellation: customer data deadline exceeded
                var signup = await _signupRepo.GetActiveByGsrnAsync(change.Gsrn, ct);
                if (signup?.ProcessRequestId is not null)
                {
                    var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                    await stateMachine.MarkAutoCancelledAsync(
                        signup.ProcessRequestId.Value,
                        "DataHub auto-cancellation: customer data deadline exceeded (D11)",
                        ct);
                    await _onboardingService.SyncFromProcessAsync(
                        signup.ProcessRequestId.Value, "cancelled", null, ct);

                    _logger.LogWarning("RSM-004/D11: Auto-cancelled process for {Gsrn}", change.Gsrn);
                }
                else
                {
                    _logger.LogWarning("RSM-004/D11: No active signup/process found for {Gsrn}", change.Gsrn);
                }
            }
            else if (change.ReasonCode is "D31" or "E01")
            {
                // Forced transfer (D31) or stop of supply by other supplier (E01): end supply period + contract
                var effectiveDate = DateOnly.FromDateTime(change.EffectiveDate.UtcDateTime);
                var endReason = change.ReasonCode == "D31" ? "forced_transfer" : "other_supplier_takeover";
                await _portfolioRepo.EndSupplyPeriodAsync(change.Gsrn, effectiveDate, endReason, ct);
                await _portfolioRepo.EndContractAsync(change.Gsrn, effectiveDate, ct);

                _logger.LogWarning("RSM-004/{ReasonCode}: Ended supply for {Gsrn}, effective {Date}",
                    change.ReasonCode, change.Gsrn, effectiveDate);
            }
            else if (change.ReasonCode == "E03")
            {
                // Stop of supply notification
                var effectiveDate = DateOnly.FromDateTime(change.EffectiveDate.UtcDateTime);
                await _portfolioRepo.EndSupplyPeriodAsync(change.Gsrn, effectiveDate, "stop_of_supply", ct);
                await _portfolioRepo.EndContractAsync(change.Gsrn, effectiveDate, ct);

                _logger.LogInformation("RSM-004/E03: Stop of supply for {Gsrn}, effective {Date}",
                    change.Gsrn, effectiveDate);
            }
            else if (change.ReasonCode == "D34")
            {
                _logger.LogInformation("RSM-004/D34: Correction accepted for {Gsrn}", change.Gsrn);
            }
            else if (change.ReasonCode == "D35")
            {
                _logger.LogWarning("RSM-004/D35: Correction rejected for {Gsrn}", change.Gsrn);
            }
            else if (change.ReasonCode == "E20")
            {
                _logger.LogInformation("RSM-004/E20: End of supply stop notification for {Gsrn}", change.Gsrn);
            }
            else if (change.ReasonCode == "D46")
            {
                _logger.LogInformation(
                    "RSM-004/D46: Special rules for start of supply on {Gsrn}, effective {Date}",
                    change.Gsrn, change.EffectiveDate);
            }
            else if (change.NewGridAreaCode is not null)
            {
                var priceArea = CimJsonParser.GridAreaToPriceArea.GetValueOrDefault(change.NewGridAreaCode, "DK1");
                await _portfolioRepo.UpdateMeteringPointGridAreaAsync(
                    change.Gsrn, change.NewGridAreaCode, priceArea, ct);

                _logger.LogInformation("RSM-004: Updated grid area for {Gsrn} to {GridArea}",
                    change.Gsrn, change.NewGridAreaCode);
            }
            else
            {
                _logger.LogInformation("RSM-004: Received change notification for {Gsrn}, no grid area update",
                    change.Gsrn);
            }
        }
        else
        {
            _logger.LogInformation("Received MasterData message type {Type}, not handled yet", message.MessageType);
        }
    }

    private async Task ProcessChargesAsync(DataHubMessage message, CancellationToken ct)
    {
        if (message.MessageType is "GRID-TARIFF")
        {
            var tariffData = _parser.ParseGridTariff(message.RawPayload);

            await _portfolioRepo.EnsureGridAreaAsync(
                tariffData.GridAreaCode, tariffData.ChargeOwnerId,
                $"Grid {tariffData.GridAreaCode}", CimJsonParser.GridAreaToPriceArea.GetValueOrDefault(tariffData.GridAreaCode, "DK1"), ct);

            await _tariffRepo.SeedGridTariffAsync(
                tariffData.GridAreaCode, tariffData.TariffType, tariffData.ValidFrom, tariffData.Rates, ct);

            await _tariffRepo.SeedSubscriptionAsync(
                tariffData.GridAreaCode, tariffData.SubscriptionType, tariffData.SubscriptionAmountPerMonth,
                tariffData.ValidFrom, ct);

            _logger.LogInformation(
                "GRID-TARIFF: Seeded {RateCount} hourly rates + subscription for grid area {GridArea}",
                tariffData.Rates.Count, tariffData.GridAreaCode);
        }
        else
        {
            _logger.LogInformation("Received Charges message {Type} — not handled yet", message.MessageType);
        }
    }

    private Task ProcessAggregationsAsync(DataHubMessage message, CancellationToken ct)
    {
        var aggregation = _parser.ParseRsm014(message.RawPayload);

        _logger.LogInformation(
            "RSM-014: Received aggregation for grid area {GridArea}, period {Start}–{End}, total {TotalKwh} kWh, {PointCount} points",
            aggregation.GridAreaCode, aggregation.PeriodStart, aggregation.PeriodEnd,
            aggregation.TotalKwh, aggregation.Points.Count);

        return Task.CompletedTask;
    }
}
