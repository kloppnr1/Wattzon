using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class QueuePollerService : BackgroundService
{
    private readonly IDataHubClient _client;
    private readonly ICimParser _parser;
    private readonly IMeteringDataRepository _meteringRepo;
    private readonly IPortfolioRepository _portfolioRepo;
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
        IMessageLog messageLog,
        ILogger<QueuePollerService> logger,
        TimeSpan? pollInterval = null)
    {
        _client = client;
        _parser = parser;
        _meteringRepo = meteringRepo;
        _portfolioRepo = portfolioRepo;
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
        catch (Exception ex) when (ex is FormatException or ArgumentException or System.Text.Json.JsonException)
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
            catch (InvalidOperationException)
            {
                _logger.LogInformation("Metering point {Gsrn} already exists, skipping create", masterData.MeteringPointId);
            }

            await _portfolioRepo.ActivateMeteringPointAsync(
                masterData.MeteringPointId, masterData.SupplyStart.UtcDateTime, ct);
            await _portfolioRepo.CreateSupplyPeriodAsync(
                masterData.MeteringPointId, DateOnly.FromDateTime(masterData.SupplyStart.UtcDateTime), ct);

            _logger.LogInformation("RSM-007: Activated metering point {Gsrn}, supply from {Start}",
                masterData.MeteringPointId, masterData.SupplyStart);
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
