namespace DataHub.Settlement.Application.Messaging;

public interface IMessageLog
{
    Task<bool> IsProcessedAsync(string messageId, CancellationToken ct);
    Task MarkProcessedAsync(string messageId, CancellationToken ct);
    Task RecordInboundAsync(string messageId, string messageType, string? correlationId, string queueName, int payloadSize, CancellationToken ct);
    Task MarkInboundStatusAsync(string messageId, string status, string? errorDetails, CancellationToken ct);
    Task DeadLetterAsync(string messageId, string queueName, string errorReason, string rawPayload, CancellationToken ct);
}
