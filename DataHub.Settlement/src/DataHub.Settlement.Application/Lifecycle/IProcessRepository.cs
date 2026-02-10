namespace DataHub.Settlement.Application.Lifecycle;

public interface IProcessRepository
{
    Task<ProcessRequest> CreateAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct);
    Task<ProcessRequest> CreateWithEventAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct);
    Task<ProcessRequest?> GetAsync(Guid id, CancellationToken ct);
    Task<ProcessRequest?> GetByCorrelationIdAsync(string correlationId, CancellationToken ct);
    Task UpdateStatusAsync(Guid id, string status, string? correlationId, CancellationToken ct);
    Task TransitionWithEventAsync(Guid id, string newStatus, string expectedStatus, string? correlationId, string eventType, CancellationToken ct);
    Task AddEventAsync(Guid processRequestId, string eventType, string? payload, string? source, CancellationToken ct);
    Task<IReadOnlyList<ProcessEvent>> GetEventsAsync(Guid processRequestId, CancellationToken ct);
    Task<IReadOnlyList<ProcessRequest>> GetByStatusAsync(string status, CancellationToken ct);
    Task<bool> HasActiveByGsrnAsync(string gsrn, CancellationToken ct);
}
