using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
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
    private readonly ITariffRepository _tariffRepo;
    private readonly IMessageLog _messageLog;
    private readonly MasterDataMessageHandler _masterDataHandler;
    private readonly Settlement.SettlementTriggerService? _settlementTrigger;
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
        ITariffRepository tariffRepo,
        IMessageLog messageLog,
        MasterDataMessageHandler masterDataHandler,
        ILogger<QueuePollerService> logger,
        SettlementMetrics? metrics = null,
        TimeSpan? pollInterval = null,
        Settlement.SettlementTriggerService? settlementTrigger = null)
    {
        _client = client;
        _parser = parser;
        _meteringRepo = meteringRepo;
        _portfolioRepo = portfolioRepo;
        _tariffRepo = tariffRepo;
        _messageLog = messageLog;
        _masterDataHandler = masterDataHandler;
        _metrics = metrics ?? new SettlementMetrics();
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _settlementTrigger = settlementTrigger;
    }

    private static readonly TimeSpan PerQueueTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue poller starting — polling {QueueCount} queues", AllQueues.Length);

        while (!stoppingToken.IsCancellationRequested)
        {
            var anyProcessed = false;

            foreach (var queue in AllQueues)
            {
                try
                {
                    using var queueCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    queueCts.CancelAfter(PerQueueTimeout);
                    var processed = await PollQueueAsync(queue, queueCts.Token);
                    if (processed) anyProcessed = true;
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Queue {Queue} poll timed out after {Timeout}s — skipping to next queue",
                        queue, PerQueueTimeout.TotalSeconds);
                }
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
        // Normalize message type once at the entry point — handles "rsm-022", "RSM022", etc.
        var normalizedType = RsmMessageTypes.Normalize(message.MessageType);

        return queue switch
        {
            QueueName.Timeseries => ProcessTimeseriesAsync(message, ct),
            QueueName.MasterData => _masterDataHandler.HandleAsync(message, normalizedType, ct),
            QueueName.Charges => ProcessChargesAsync(message, normalizedType, ct),
            QueueName.Aggregations => ProcessAggregationsAsync(message, ct),
            _ => Task.CompletedTask,
        };
    }

    private async Task ProcessTimeseriesAsync(DataHubMessage message, CancellationToken ct)
    {
        var seriesList = _parser.ParseRsm012(message.RawPayload);
        var processedGsrns = new HashSet<string>();

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
            processedGsrns.Add(series.MeteringPointId);

            if (changedCount > 0)
            {
                _logger.LogInformation(
                    "Detected {ChangedCount} corrected readings for {Gsrn} — correction settlement may be needed",
                    changedCount, series.MeteringPointId);
            }
        }

        // Trigger settlement for each affected GSRN after storing metering data
        if (_settlementTrigger is not null)
        {
            foreach (var gsrn in processedGsrns)
            {
                try
                {
                    await _settlementTrigger.TrySettleAsync(gsrn, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "RSM-012 triggered settlement failed for GSRN {Gsrn}", gsrn);
                }
            }
        }
    }

    private async Task ProcessChargesAsync(DataHubMessage message, string normalizedType, CancellationToken ct)
    {
        if (normalizedType == RsmMessageTypes.PriceSeries)
        {
            var tariffData = _parser.ParseRsm034PriceSeries(message.RawPayload);

            await _portfolioRepo.EnsureGridAreaAsync(
                tariffData.GridAreaCode, tariffData.ChargeOwnerId,
                $"Grid {tariffData.GridAreaCode}", CimJsonParser.GridAreaToPriceArea.GetValueOrDefault(tariffData.GridAreaCode, "DK1"), ct);

            await _tariffRepo.SeedGridTariffAsync(
                tariffData.GridAreaCode, tariffData.TariffType, tariffData.ValidFrom, tariffData.Rates, ct);

            await _tariffRepo.SeedSubscriptionAsync(
                tariffData.GridAreaCode, tariffData.SubscriptionType, tariffData.SubscriptionAmountPerMonth,
                tariffData.ValidFrom, ct);

            if (tariffData.ElectricityTaxRate is not null)
            {
                await _tariffRepo.SeedElectricityTaxAsync(
                    tariffData.ElectricityTaxRate.Value, tariffData.ValidFrom, ct);
            }

            _logger.LogInformation(
                "RSM-034: Seeded {RateCount} hourly rates + subscription for grid area {GridArea}",
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
