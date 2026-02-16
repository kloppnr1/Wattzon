using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class QueuePoller<THandler> : BackgroundService where THandler : IMessageHandler
{
    private readonly IDataHubClient _client;
    private readonly THandler _handler;
    private readonly IMessageLog _messageLog;
    private readonly SettlementMetrics _metrics;
    private readonly ILogger<QueuePoller<THandler>> _logger;
    private readonly TimeSpan _pollInterval;

    private static readonly TimeSpan PerQueueTimeout = TimeSpan.FromSeconds(30);

    public QueuePoller(
        IDataHubClient client,
        THandler handler,
        IMessageLog messageLog,
        SettlementMetrics metrics,
        ILogger<QueuePoller<THandler>> logger,
        TimeSpan? pollInterval = null)
    {
        _client = client;
        _handler = handler;
        _messageLog = messageLog;
        _metrics = metrics;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public QueueName Queue => _handler.Queue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue poller starting for {Queue} (interval {Interval}ms)",
            _handler.Queue, _pollInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var queueCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                queueCts.CancelAfter(PerQueueTimeout);
                var processed = await PollQueueAsync(queueCts.Token);

                if (!processed)
                {
                    await Task.Delay(_pollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Queue {Queue} poll timed out after {Timeout}s",
                    _handler.Queue, PerQueueTimeout.TotalSeconds);
            }
        }
    }

    public async Task<bool> PollQueueAsync(CancellationToken ct)
    {
        var queue = _handler.Queue;
        var message = await _client.PeekAsync(queue, ct);
        if (message is null)
            return false;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = message.MessageId,
            ["MessageType"] = message.MessageType,
            ["CorrelationId"] = message.CorrelationId,
            ["Queue"] = queue.ToString(),
        });

        _logger.LogInformation("Processing message {MessageId} ({MessageType}) from {Queue}",
            message.MessageId, message.MessageType, queue);

        await _messageLog.RecordInboundAsync(
            message.MessageId, message.MessageType, message.CorrelationId,
            queue.ToString(), message.RawPayload.Length, message.RawPayload, ct);

        if (!await _messageLog.TryClaimForProcessingAsync(message.MessageId, ct))
        {
            _logger.LogInformation("Message {MessageId} already claimed by another poller, skipping", message.MessageId);
            await _client.DequeueAsync(message.MessageId, ct);
            return true;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await _handler.HandleAsync(message, ct);

            sw.Stop();
            _metrics.RecordMessageProcessed(message.MessageType, queue.ToString());
            _metrics.RecordMessageDuration(sw.Elapsed.TotalMilliseconds, message.MessageType);

            await _messageLog.MarkInboundStatusAsync(message.MessageId, "processed", null, ct);
            await _client.DequeueAsync(message.MessageId, ct);

            _logger.LogInformation("Message {MessageId} processed successfully in {DurationMs}ms",
                message.MessageId, sw.Elapsed.TotalMilliseconds);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            _metrics.RecordMessageDeadLettered(message.MessageType, queue.ToString());
            _logger.LogWarning(ex, "Message {MessageId} failed to parse, dead-lettering", message.MessageId);
            await _messageLog.DeadLetterAsync(message.MessageId, queue.ToString(), ex.Message, message.RawPayload, ct);
            await _messageLog.MarkInboundStatusAsync(message.MessageId, "dead_lettered", ex.Message, ct);
            await _client.DequeueAsync(message.MessageId, ct);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _metrics.RecordMessageFailed(message.MessageType, queue.ToString());
            _logger.LogWarning(ex,
                "Message {MessageId} hit a state transition conflict, will retry on next poll",
                message.MessageId);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordMessageDeadLettered(message.MessageType, queue.ToString());
            _logger.LogError(ex, "Message {MessageId} failed with unexpected error, dead-lettering", message.MessageId);
            await _messageLog.DeadLetterAsync(message.MessageId, queue.ToString(), ex.Message, message.RawPayload, ct);
            await _messageLog.MarkInboundStatusAsync(message.MessageId, "dead_lettered", ex.Message, ct);
            await _client.DequeueAsync(message.MessageId, ct);
            return true;
        }
    }

    public Task ReprocessMessageAsync(DataHubMessage message, CancellationToken ct)
    {
        return _handler.HandleAsync(message, ct);
    }
}
