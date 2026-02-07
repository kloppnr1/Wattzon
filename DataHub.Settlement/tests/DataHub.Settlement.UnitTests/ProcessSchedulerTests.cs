using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Infrastructure.Lifecycle;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class ProcessSchedulerTests
{
    private readonly ProcessStateMachineTests.InMemoryProcessRepository _processRepo = new();
    private readonly TestClock _clock = new();

    private ProcessSchedulerService CreateSut() => new(
        _processRepo, _clock,
        NullLogger<ProcessSchedulerService>.Instance);

    [Fact]
    public async Task Effectuates_process_on_effective_date()
    {
        _clock.Today = new DateOnly(2025, 2, 1);
        var sm = new ProcessStateMachine(_processRepo, _clock);

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 2, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        // Process is in effectuation_pending
        var before = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        before!.Status.Should().Be("effectuation_pending");

        var sut = CreateSut();
        await sut.RunTickAsync(CancellationToken.None);

        var after = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("completed");
    }

    [Fact]
    public async Task Skips_future_dated_process()
    {
        _clock.Today = new DateOnly(2025, 1, 15);
        var sm = new ProcessStateMachine(_processRepo, _clock);

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 2, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);

        var sut = CreateSut();
        await sut.RunTickAsync(CancellationToken.None);

        var after = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        after!.Status.Should().Be("effectuation_pending");
    }
}
