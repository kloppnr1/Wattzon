namespace DataHub.Settlement.Application.Lifecycle;

public sealed class ProcessStateMachine
{
    private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new()
    {
        ["pending"] = ["sent_to_datahub"],
        ["sent_to_datahub"] = ["acknowledged", "rejected"],
        ["acknowledged"] = ["effectuation_pending"],
        ["effectuation_pending"] = ["completed"],
    };

    private readonly IProcessRepository _repository;

    public ProcessStateMachine(IProcessRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProcessRequest> CreateRequestAsync(
        string gsrn, string processType, DateOnly effectiveDate, CancellationToken ct)
    {
        var request = await _repository.CreateAsync(processType, gsrn, effectiveDate, ct);
        await _repository.AddEventAsync(request.Id, "created", null, "system", ct);
        return request;
    }

    public async Task MarkSentAsync(Guid requestId, string correlationId, CancellationToken ct)
    {
        await TransitionAsync(requestId, "sent_to_datahub", correlationId, "sent", ct);
    }

    public async Task MarkAcknowledgedAsync(Guid requestId, CancellationToken ct)
    {
        await TransitionAsync(requestId, "acknowledged", null, "acknowledged", ct);
        // Auto-transition to effectuation_pending
        await TransitionAsync(requestId, "effectuation_pending", null, "awaiting_effectuation", ct);
    }

    public async Task MarkCompletedAsync(Guid requestId, CancellationToken ct)
    {
        await TransitionAsync(requestId, "completed", null, "completed", ct);
    }

    private async Task TransitionAsync(Guid requestId, string newStatus, string? correlationId, string eventType, CancellationToken ct)
    {
        var request = await _repository.GetAsync(requestId, ct)
            ?? throw new InvalidOperationException($"Process request {requestId} not found");

        if (!ValidTransitions.TryGetValue(request.Status, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new InvalidOperationException(
                $"Invalid transition from '{request.Status}' to '{newStatus}' for process {requestId}");
        }

        await _repository.UpdateStatusAsync(requestId, newStatus, correlationId, ct);
        await _repository.AddEventAsync(requestId, eventType, null, "system", ct);
    }
}
