using DataHub.Settlement.Domain;

namespace DataHub.Settlement.Application.Lifecycle;

public sealed class ProcessStateMachine
{
    private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new()
    {
        ["pending"] = ["sent_to_datahub", "cancelled"],
        ["sent_to_datahub"] = ["acknowledged", "rejected"],
        ["acknowledged"] = ["effectuation_pending"],
        ["effectuation_pending"] = ["completed", "cancellation_pending"],
        ["cancellation_pending"] = ["cancelled", "effectuation_pending"],
        ["completed"] = ["offboarding"],
        ["offboarding"] = ["final_settled"],
    };

    private readonly IProcessRepository _repository;
    private readonly IClock _clock;

    public ProcessStateMachine(IProcessRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<ProcessRequest> CreateRequestAsync(
        string gsrn, string processType, DateOnly effectiveDate, CancellationToken ct)
    {
        return await _repository.CreateWithEventAsync(processType, gsrn, effectiveDate, ct);
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
        // RSM-022 is the authoritative signal from DataHub that supply has started
        // No temporal guard - we trust DataHub's activation signal
        await TransitionAsync(requestId, "completed", null, "completed", ct);
    }

    public async Task MarkRejectedAsync(Guid requestId, string? reason, CancellationToken ct)
    {
        await TransitionAsync(requestId, "rejected", null, "rejected", ct);
        if (reason != null)
        {
            await _repository.AddEventAsync(requestId, "rejection_reason", reason, "datahub", ct);
        }
    }

    public async Task MarkCancellationSentAsync(Guid requestId, CancellationToken ct)
    {
        await TransitionAsync(requestId, "cancellation_pending", null, "cancellation_sent", ct);
    }

    public async Task RevertCancellationAsync(Guid requestId, string? reason, CancellationToken ct)
    {
        await TransitionAsync(requestId, "effectuation_pending", null, "cancellation_rejected", ct);
        if (reason != null)
            await _repository.AddEventAsync(requestId, "cancellation_rejection_reason", reason, "datahub", ct);
    }

    public async Task MarkCancelledAsync(Guid requestId, string? reason, CancellationToken ct)
    {
        await TransitionAsync(requestId, "cancelled", null, "cancelled", ct);
        if (reason != null)
        {
            await _repository.AddEventAsync(requestId, "cancellation_reason", reason, "system", ct);
        }
    }

    public async Task MarkOffboardingAsync(Guid requestId, CancellationToken ct)
    {
        await TransitionAsync(requestId, "offboarding", null, "offboarding_started", ct);
    }

    public async Task MarkFinalSettledAsync(Guid requestId, CancellationToken ct)
    {
        await TransitionAsync(requestId, "final_settled", null, "final_settled", ct);
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

        await _repository.TransitionWithEventAsync(requestId, newStatus, request.Status, correlationId, eventType, ct);
    }
}
