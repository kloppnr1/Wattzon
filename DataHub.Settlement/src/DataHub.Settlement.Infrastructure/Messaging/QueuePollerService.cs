using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Parsing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class QueuePollerService : BackgroundService
{
    private readonly IDataHubClient _client;
    private readonly ICimParser _parser;
    private readonly IMeteringDataRepository _meteringRepo;
    private readonly IMessageLog _messageLog;
    private readonly ILogger<QueuePollerService> _logger;
    private readonly TimeSpan _pollInterval;

    public QueuePollerService(
        IDataHubClient client,
        ICimParser parser,
        IMeteringDataRepository meteringRepo,
        IMessageLog messageLog,
        ILogger<QueuePollerService> logger,
        TimeSpan? pollInterval = null)
    {
        _client = client;
        _parser = parser;
        _meteringRepo = meteringRepo;
        _messageLog = messageLog;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue poller starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await PollQueueAsync(QueueName.Timeseries, stoppingToken);
            if (!processed)
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
            await ProcessMessageAsync(message, ct);

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

    private async Task ProcessMessageAsync(DataHubMessage message, CancellationToken ct)
    {
        var seriesList = _parser.ParseRsm012(message.RawPayload);

        foreach (var series in seriesList)
        {
            var rows = series.Points.Select(p => new MeteringDataRow(
                p.Timestamp.UtcDateTime, series.Resolution, p.QuantityKwh, p.QualityCode, series.TransactionId)).ToList();

            await _meteringRepo.StoreTimeSeriesAsync(series.MeteringPointId, rows, ct);
        }
    }
}
