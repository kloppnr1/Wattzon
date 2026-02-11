using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Domain;
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
    private readonly IClock _clock;
    private readonly IMessageLog _messageLog;
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
        IClock clock,
        IMessageLog messageLog,
        ILogger<QueuePollerService> logger,
        TimeSpan? pollInterval = null)
    {
        _client = client;
        _parser = parser;
        _meteringRepo = meteringRepo;
        _portfolioRepo = portfolioRepo;
        _processRepo = processRepo;
        _signupRepo = signupRepo;
        _onboardingService = onboardingService;
        _clock = clock;
        _messageLog = messageLog;
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

        _logger.LogInformation("Processing message {MessageId} ({MessageType}) from {Queue}",
            message.MessageId, message.MessageType, queue);

        // Record inbound
        await _messageLog.RecordInboundAsync(
            message.MessageId, message.MessageType, message.CorrelationId,
            queue.ToString(), message.RawPayload.Length, ct);

        // Idempotency check
        if (await _messageLog.IsProcessedAsync(message.MessageId, ct))
        {
            _logger.LogInformation("Message {MessageId} already processed, skipping", message.MessageId);
            await _client.DequeueAsync(message.MessageId, ct);
            return true;
        }

        try
        {
            await ProcessMessageAsync(message, queue, ct);

            // Mark processed BEFORE dequeue (at-least-once delivery)
            await _messageLog.MarkProcessedAsync(message.MessageId, ct);
            await _messageLog.MarkInboundStatusAsync(message.MessageId, "processed", null, ct);
            await _client.DequeueAsync(message.MessageId, ct);

            _logger.LogInformation("Message {MessageId} processed successfully", message.MessageId);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // Parse errors → dead-letter + dequeue (free the queue)
            _logger.LogWarning(ex, "Message {MessageId} failed to parse, dead-lettering", message.MessageId);
            await _messageLog.DeadLetterAsync(message.MessageId, queue.ToString(), ex.Message, message.RawPayload, ct);
            await _messageLog.MarkInboundStatusAsync(message.MessageId, "dead_lettered", ex.Message, ct);
            await _client.DequeueAsync(message.MessageId, ct);
            return true;
        }
        // DB errors propagate up → message NOT dequeued → retry on next poll
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
            var rows = series.Points.Select(p => new MeteringDataRow(
                p.Timestamp.UtcDateTime, series.Resolution, p.QuantityKwh, p.QualityCode, series.TransactionId)).ToList();

            var changedCount = await _meteringRepo.StoreTimeSeriesWithHistoryAsync(series.MeteringPointId, rows, ct);

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
        if (message.MessageType is "RSM-007" or "rsm-007" or "RSM007")
        {
            var masterData = _parser.ParseRsm007(message.RawPayload);

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

            // RSM-007 is the authoritative activation signal from DataHub
            // This is the ONLY place where processes are marked "completed"
            var signup = await _signupRepo.GetActiveByGsrnAsync(masterData.MeteringPointId, ct);
            if (signup is not null && signup.ProcessRequestId.HasValue)
            {
                var process = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
                if (process is not null && process.Status is "cancellation_pending" or "cancelled")
                {
                    _logger.LogInformation(
                        "RSM-007: Skipping activation for process {ProcessId} — process is {Status}",
                        process.Id, process.Status);
                }
                else
                {
                    var effectiveDate = DateOnly.FromDateTime(masterData.SupplyStart.UtcDateTime);

                    // 1. Mark process completed (supply has started per DataHub)
                    var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                    await stateMachine.MarkCompletedAsync(signup.ProcessRequestId.Value, ct);

                    // 2. Sync signup status → creates or links Customer entity
                    await _onboardingService.SyncFromProcessAsync(signup.ProcessRequestId.Value, "completed", null, ct);

                    // 3. Reload signup to get the customer_id (just created or linked)
                    // Note: Can't use GetActiveByGsrnAsync here because it filters OUT status='active'
                    signup = await _signupRepo.GetByIdAsync(signup.Id, ct);

                    if (signup?.CustomerId is not null)
                    {
                        // 4. Create Contract and SupplyPeriod
                        await _portfolioRepo.CreateContractAsync(
                            signup.CustomerId.Value, masterData.MeteringPointId, signup.ProductId,
                            "quarterly", "aconto", effectiveDate, ct);

                        await _portfolioRepo.CreateSupplyPeriodAsync(masterData.MeteringPointId, effectiveDate, ct);

                        _logger.LogInformation(
                            "RSM-007: Activated portfolio for signup {SignupNumber}, GSRN {Gsrn}, supply from {Start}",
                            signup.SignupNumber, masterData.MeteringPointId, masterData.SupplyStart);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "RSM-007: Signup {SignupNumber} for GSRN {Gsrn} has no customer after activation — portfolio not created",
                            signup?.SignupNumber ?? "unknown", masterData.MeteringPointId);
                    }
                }
            }
            else if (signup is null)
            {
                // No signup — create supply period without contract (pre-onboarding metering points)
                await _portfolioRepo.CreateSupplyPeriodAsync(
                    masterData.MeteringPointId, DateOnly.FromDateTime(masterData.SupplyStart.UtcDateTime), ct);

                _logger.LogInformation("RSM-007: Activated metering point {Gsrn}, supply from {Start} (no signup)",
                    masterData.MeteringPointId, masterData.SupplyStart);
            }
            else
            {
                _logger.LogWarning(
                    "RSM-007: Signup {SignupNumber} for GSRN {Gsrn} has no process request — portfolio not created",
                    signup.SignupNumber, masterData.MeteringPointId);
            }
        }
        else if (message.MessageType is "RSM-009" or "rsm-009" or "RSM009")
        {
            var receipt = _parser.ParseRsm009(message.RawPayload);

            var process = await _processRepo.GetByCorrelationIdAsync(receipt.CorrelationId, ct);

            // If no match on datahub_correlation_id, try cancel_correlation_id (RSM-009 for cancellation ack)
            if (process is null)
            {
                process = await _processRepo.GetByCancelCorrelationIdAsync(receipt.CorrelationId, ct);
                if (process is not null && process.Status == "cancellation_pending")
                {
                    var cancelStateMachine = new ProcessStateMachine(_processRepo, _clock);
                    await cancelStateMachine.MarkCancelledAsync(process.Id, "Cancellation acknowledged by DataHub", ct);
                    await _onboardingService.SyncFromProcessAsync(process.Id, "cancelled", null, ct);

                    _logger.LogInformation("RSM-009: Cancellation acknowledged for process {ProcessId}, GSRN {Gsrn}",
                        process.Id, process.Gsrn);
                    return;
                }

                _logger.LogWarning("RSM-009: No process found for correlation {CorrelationId}", receipt.CorrelationId);
                return;
            }

            var stateMachine = new ProcessStateMachine(_processRepo, _clock);

            if (receipt.Accepted)
            {
                await stateMachine.MarkAcknowledgedAsync(process.Id, ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "effectuation_pending", null, ct);

                _logger.LogInformation("RSM-009: Process {ProcessId} accepted for GSRN {Gsrn}",
                    process.Id, process.Gsrn);
            }
            else
            {
                var reason = receipt.RejectionReason ?? receipt.RejectionCode ?? "Unknown rejection";
                await stateMachine.MarkRejectedAsync(process.Id, reason, ct);
                await _onboardingService.SyncFromProcessAsync(process.Id, "rejected", reason, ct);

                _logger.LogWarning("RSM-009: Process {ProcessId} rejected for GSRN {Gsrn}: {Reason}",
                    process.Id, process.Gsrn, reason);
            }
        }
        else if (message.MessageType is "RSM-004" or "rsm-004" or "RSM004")
        {
            var change = _parser.ParseRsm004(message.RawPayload);

            if (change.NewGridAreaCode is not null)
            {
                var priceArea = change.NewGridAreaCode.StartsWith("7") ? "DK2" : "DK1";
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

    private Task ProcessChargesAsync(DataHubMessage message, CancellationToken ct)
    {
        _logger.LogInformation("Received Charges message {Type} — stored for future processing", message.MessageType);
        return Task.CompletedTask;
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
