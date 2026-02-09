using DataHub.Settlement.Application.Lifecycle;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class ProcessStateMachineTemporalTests
{
    private readonly ProcessStateMachineTests.InMemoryProcessRepository _repo = new();
    private readonly TestClock _clock = new();

    private ProcessStateMachine CreateSut() => new(_repo, _clock);

    // NOTE: Temporal guard on MarkCompletedAsync was intentionally removed.
    // RSM-007 is the ONLY signal that marks processes "completed", and we trust
    // DataHub to only send RSM-007 when appropriate. See V022 architecture decision.

    [Fact]
    public async Task Can_effectuate_on_effective_date()
    {
        _clock.Today = new DateOnly(2025, 2, 1);
        var sut = CreateSut();

        var request = await sut.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 2, 1), CancellationToken.None);
        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        await sut.MarkCompletedAsync(request.Id, CancellationToken.None);

        var after = await _repo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("completed");
    }

    [Fact]
    public async Task Existing_transitions_unaffected_by_clock()
    {
        _clock.Today = new DateOnly(2025, 12, 31);
        var sut = CreateSut();

        var request = await sut.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 1, 1), CancellationToken.None);
        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);

        var afterSent = await _repo.GetAsync(request.Id, CancellationToken.None);
        afterSent!.Status.Should().Be("sent_to_datahub");

        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        var afterAck = await _repo.GetAsync(request.Id, CancellationToken.None);
        afterAck!.Status.Should().Be("effectuation_pending");

        await sut.MarkCompletedAsync(request.Id, CancellationToken.None);
        var afterComplete = await _repo.GetAsync(request.Id, CancellationToken.None);
        afterComplete!.Status.Should().Be("completed");
    }
}
