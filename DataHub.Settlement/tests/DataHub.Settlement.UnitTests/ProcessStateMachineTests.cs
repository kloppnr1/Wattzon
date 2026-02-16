using DataHub.Settlement.Application.Lifecycle;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class ProcessStateMachineTests
{
    private readonly InMemoryProcessRepository _repo = new();
    private readonly TestClock _clock = new();

    private ProcessStateMachine CreateSut() => new(_repo, _clock);

    [Fact]
    public async Task CreateRequest_creates_pending_process()
    {
        var sut = CreateSut();

        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);

        request.Status.Should().Be("pending");
        request.Gsrn.Should().Be("571313100000012345");
    }

    [Fact]
    public async Task Full_sunshine_path_transitions_correctly()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);

        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        var afterSent = await _repo.GetAsync(request.Id, CancellationToken.None);
        afterSent!.Status.Should().Be("sent_to_datahub");
        afterSent.DatahubCorrelationId.Should().Be("corr-123");

        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        var afterAck = await _repo.GetAsync(request.Id, CancellationToken.None);
        afterAck!.Status.Should().Be("effectuation_pending");

        await sut.MarkCompletedAsync(request.Id, CancellationToken.None);
        var afterComplete = await _repo.GetAsync(request.Id, CancellationToken.None);
        afterComplete!.Status.Should().Be("completed");
    }

    [Fact]
    public async Task Invalid_transition_throws()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);

        // Cannot go directly from pending to acknowledged
        var act = () => sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid transition*");
    }

    [Fact]
    public async Task Each_transition_creates_events()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);

        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sut.MarkCompletedAsync(request.Id, CancellationToken.None);

        var events = await _repo.GetEventsAsync(request.Id, CancellationToken.None);
        events.Should().HaveCount(5); // created, sent, acknowledged, awaiting_effectuation, completed
    }

    [Fact]
    public async Task Rejection_from_sent_to_datahub_transitions_correctly()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);
        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);

        await sut.MarkRejectedAsync(request.Id, "E16: Invalid GSRN", CancellationToken.None);

        var after = await _repo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("rejected");

        var events = await _repo.GetEventsAsync(request.Id, CancellationToken.None);
        events.Should().Contain(e => e.EventType == "rejected");
        events.Should().Contain(e => e.EventType == "rejection_reason");
    }

    [Fact]
    public async Task Cancellation_from_pending_transitions_correctly()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);

        await sut.MarkCancelledAsync(request.Id, "Customer withdrew", CancellationToken.None);

        var after = await _repo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task Cancellation_from_effectuation_pending_transitions_through_cancellation_pending()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);
        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        // Step 1: Mark cancellation sent (transition to cancellation_pending)
        await sut.MarkCancellationSentAsync(request.Id, CancellationToken.None);

        var afterCancellationSent = await _repo.GetAsync(request.Id, CancellationToken.None);
        afterCancellationSent!.Status.Should().Be("cancellation_pending");

        var events = await _repo.GetEventsAsync(request.Id, CancellationToken.None);
        events.Should().Contain(e => e.EventType == "cancellation_sent");

        // Step 2: DataHub acknowledges cancellation (transition to cancelled)
        await sut.MarkCancelledAsync(request.Id, "Cancellation acknowledged by DataHub", CancellationToken.None);

        var afterCancelled = await _repo.GetAsync(request.Id, CancellationToken.None);
        afterCancelled!.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task Cancellation_from_effectuation_pending_rejects_direct_cancelled()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);
        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        // Direct MarkCancelledAsync from effectuation_pending should fail (must go through cancellation_pending)
        var act = () => sut.MarkCancelledAsync(request.Id, "Cancelled by user", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid transition*");
    }

    [Fact]
    public async Task Cancellation_from_pending_still_works()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);

        // Direct cancellation from pending should still work (no DataHub involvement)
        await sut.MarkCancelledAsync(request.Id, "Cancelled by user before sending", CancellationToken.None);

        var after = await _repo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("cancelled");

        var events = await _repo.GetEventsAsync(request.Id, CancellationToken.None);
        events.Should().Contain(e => e.EventType == "cancelled");
        events.Should().Contain(e => e.EventType == "cancellation_reason");
    }

    [Fact]
    public async Task Offboarding_from_completed_transitions_correctly()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);
        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sut.MarkCompletedAsync(request.Id, CancellationToken.None);

        await sut.MarkOffboardingAsync(request.Id, CancellationToken.None);

        var after = await _repo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("offboarding");
    }

    [Fact]
    public async Task Final_settled_from_offboarding_transitions_correctly()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);
        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sut.MarkCompletedAsync(request.Id, CancellationToken.None);
        await sut.MarkOffboardingAsync(request.Id, CancellationToken.None);

        await sut.MarkFinalSettledAsync(request.Id, CancellationToken.None);

        var after = await _repo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("final_settled");

        var events = await _repo.GetEventsAsync(request.Id, CancellationToken.None);
        events.Should().Contain(e => e.EventType == "final_settled");
    }

    [Fact]
    public async Task Cannot_offboard_from_pending()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 2, 1), CancellationToken.None);

        var act = () => sut.MarkOffboardingAsync(request.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid transition*");
    }

    /// <summary>In-memory implementation of IProcessRepository for unit testing.</summary>
    internal sealed class InMemoryProcessRepository : IProcessRepository
    {
        private readonly Dictionary<Guid, ProcessRequest> _requests = new();
        private readonly List<ProcessEvent> _events = new();

        public Task<ProcessRequest> CreateAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
        {
            var request = new ProcessRequest(Guid.NewGuid(), processType, gsrn, "pending", effectiveDate, null);
            _requests[request.Id] = request;
            return Task.FromResult(request);
        }

        public async Task<ProcessRequest> CreateWithEventAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
        {
            var request = await CreateAsync(processType, gsrn, effectiveDate, ct);
            await AddEventAsync(request.Id, "created", null, "system", ct);
            return request;
        }

        public Task<ProcessRequest?> GetAsync(Guid id, CancellationToken ct)
        {
            _requests.TryGetValue(id, out var request);
            return Task.FromResult(request);
        }

        public Task<ProcessRequest?> GetByCorrelationIdAsync(string correlationId, CancellationToken ct)
        {
            var request = _requests.Values.FirstOrDefault(r => r.DatahubCorrelationId == correlationId);
            return Task.FromResult(request);
        }

        public Task UpdateStatusAsync(Guid id, string status, string? correlationId, CancellationToken ct)
        {
            var existing = _requests[id];
            _requests[id] = existing with
            {
                Status = status,
                DatahubCorrelationId = correlationId ?? existing.DatahubCorrelationId,
            };
            return Task.CompletedTask;
        }

        public async Task TransitionWithEventAsync(Guid id, string newStatus, string expectedStatus, string? correlationId, string eventType, CancellationToken ct)
        {
            await UpdateStatusAsync(id, newStatus, correlationId, ct);
            await AddEventAsync(id, eventType, null, "system", ct);
        }

        public Task AddEventAsync(Guid processRequestId, string eventType, string? payload, string? source, CancellationToken ct)
        {
            _events.Add(new ProcessEvent(Guid.NewGuid(), processRequestId, DateTime.UtcNow, eventType, payload, source));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProcessEvent>> GetEventsAsync(Guid processRequestId, CancellationToken ct)
        {
            var events = _events.Where(e => e.ProcessRequestId == processRequestId).ToList();
            return Task.FromResult<IReadOnlyList<ProcessEvent>>(events);
        }

        public Task<IReadOnlyList<ProcessRequest>> GetByStatusAsync(string status, CancellationToken ct)
        {
            var matching = _requests.Values.Where(r => r.Status == status).ToList();
            return Task.FromResult<IReadOnlyList<ProcessRequest>>(matching);
        }

        public Task<bool> HasActiveByGsrnAsync(string gsrn, CancellationToken ct)
        {
            var hasActive = _requests.Values.Any(r =>
                r.Gsrn == gsrn &&
                r.Status is not ("completed" or "cancelled" or "rejected" or "final_settled"));
            return Task.FromResult(hasActive);
        }

        public Task AutoCancelAsync(Guid requestId, string expectedStatus, string reason, CancellationToken ct)
        {
            if (!_requests.TryGetValue(requestId, out var req))
                throw new InvalidOperationException($"Process request {requestId} not found");
            if (req.Status != expectedStatus)
                throw new InvalidOperationException($"Cannot auto-cancel: expected '{expectedStatus}', got '{req.Status}'");

            _requests[requestId] = req with { Status = "cancelled" };
            _events.Add(new ProcessEvent(Guid.NewGuid(), requestId, DateTime.UtcNow, "auto_cancelled", null, "datahub"));
            _events.Add(new ProcessEvent(Guid.NewGuid(), requestId, DateTime.UtcNow, "cancellation_reason", reason, "datahub"));
            return Task.CompletedTask;
        }

        public Task MarkCustomerDataReceivedAsync(string correlationId, CancellationToken ct) => Task.CompletedTask;
        public Task MarkTariffDataReceivedAsync(string correlationId, CancellationToken ct) => Task.CompletedTask;
        public Task<ProcessDetail?> GetDetailWithChecklistAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessDetail?>(null);
        public Task<IReadOnlyList<ProcessRequest>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProcessRequest>>(Array.Empty<ProcessRequest>());

        public Task<ProcessRequest?> GetCompletedByGsrnAsync(string gsrn, CancellationToken ct)
        {
            var request = _requests.Values
                .Where(r => r.Gsrn == gsrn && r.Status == "completed")
                .OrderByDescending(r => r.Id) // proxy for created_at DESC
                .FirstOrDefault();
            return Task.FromResult(request);
        }
    }
}
