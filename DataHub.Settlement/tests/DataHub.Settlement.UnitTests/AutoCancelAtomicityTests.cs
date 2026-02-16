using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Infrastructure.Lifecycle;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

/// <summary>
/// Tests that MarkAutoCancelledAsync is atomic (no intermediate cancellation_pending state).
/// </summary>
public class AutoCancelAtomicityTests
{
    [Fact]
    public async Task AutoCancel_from_effectuation_pending_goes_directly_to_cancelled()
    {
        var repo = new ProcessStateMachineTests.InMemoryProcessRepository();
        var clock = new TestClock();
        var sut = new ProcessStateMachine(repo, clock);

        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch,
            new DateOnly(2025, 2, 1), CancellationToken.None);

        await sut.MarkSentAsync(request.Id, "corr-123", CancellationToken.None);
        await sut.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        var before = await repo.GetAsync(request.Id, CancellationToken.None);
        before!.Status.Should().Be("effectuation_pending");

        // Act: auto-cancel (D11 from DataHub)
        await sut.MarkAutoCancelledAsync(request.Id, "Customer data deadline exceeded (D11)", CancellationToken.None);

        // Assert: goes directly to cancelled, never stuck in cancellation_pending
        var after = await repo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("cancelled");

        // Should have auto_cancelled + cancellation_reason events
        var events = await repo.GetEventsAsync(request.Id, CancellationToken.None);
        events.Should().Contain(e => e.EventType == "auto_cancelled");
        events.Should().Contain(e => e.EventType == "cancellation_reason");
    }

    [Fact]
    public async Task AutoCancel_from_wrong_status_throws()
    {
        var repo = new ProcessStateMachineTests.InMemoryProcessRepository();
        var clock = new TestClock();
        var sut = new ProcessStateMachine(repo, clock);

        var request = await sut.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch,
            new DateOnly(2025, 2, 1), CancellationToken.None);

        // Process is in "pending" — auto-cancel expects "effectuation_pending"
        var act = () => sut.MarkAutoCancelledAsync(request.Id, "D11 test", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
