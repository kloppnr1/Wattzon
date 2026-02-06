using DataHub.Settlement.Application.Lifecycle;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class ProcessStateMachineTests
{
    private readonly InMemoryProcessRepository _repo = new();

    private ProcessStateMachine CreateSut() => new(_repo);

    [Fact]
    public async Task CreateRequest_creates_pending_process()
    {
        var sut = CreateSut();

        var request = await sut.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), CancellationToken.None);

        request.Status.Should().Be("pending");
        request.Gsrn.Should().Be("571313100000012345");
    }

    [Fact]
    public async Task Full_sunshine_path_transitions_correctly()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), CancellationToken.None);

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
        var request = await sut.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), CancellationToken.None);

        // Cannot go directly from pending to acknowledged
        var act = () => sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid transition*");
    }

    [Fact]
    public async Task Each_transition_creates_events()
    {
        var sut = CreateSut();
        var request = await sut.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), CancellationToken.None);

        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sut.MarkCompletedAsync(request.Id, CancellationToken.None);

        var events = await _repo.GetEventsAsync(request.Id, CancellationToken.None);
        events.Should().HaveCount(5); // created, sent, acknowledged, awaiting_effectuation, completed
    }

    /// <summary>In-memory implementation of IProcessRepository for unit testing.</summary>
    private sealed class InMemoryProcessRepository : IProcessRepository
    {
        private readonly Dictionary<Guid, ProcessRequest> _requests = new();
        private readonly List<ProcessEvent> _events = new();

        public Task<ProcessRequest> CreateAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
        {
            var request = new ProcessRequest(Guid.NewGuid(), processType, gsrn, "pending", effectiveDate, null);
            _requests[request.Id] = request;
            return Task.FromResult(request);
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
    }
}
