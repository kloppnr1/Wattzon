namespace DataHub.Settlement.Application.DataHub;

public interface IDataHubClient
{
    Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct);
    Task DequeueAsync(string messageId, CancellationToken ct);
    Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct);
}

public record DataHubMessage(string MessageId, string MessageType, string? CorrelationId, string RawPayload);

public record DataHubResponse(string CorrelationId, bool Accepted, string? RejectionReason);

public enum QueueName
{
    Timeseries,
    MasterData,
    Charges,
    Aggregations,
}
