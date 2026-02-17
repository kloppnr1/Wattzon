using DataHub.Settlement.Application.Common;

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

    /// <summary>
    /// Atomically transitions a process from expectedStatus to 'cancelled' with reason event,
    /// in a single transaction. Used for DataHub D11 auto-cancellation to prevent partial state.
    /// </summary>
    Task AutoCancelAsync(Guid requestId, string expectedStatus, string reason, CancellationToken ct);

    Task MarkCustomerDataReceivedAsync(string correlationId, CancellationToken ct);
    Task MarkTariffDataReceivedAsync(string correlationId, CancellationToken ct);

    Task<ProcessDetail?> GetDetailWithChecklistAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ProcessRequest>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct);

    Task<ProcessRequest?> GetCompletedByGsrnAsync(string gsrn, CancellationToken ct);

    Task<PagedResult<ProcessListItem>> GetProcessesPagedAsync(
        string? status, string? processType, string? search,
        int page, int pageSize, CancellationToken ct);
}
