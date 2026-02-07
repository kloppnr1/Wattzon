using DataHub.Settlement.Infrastructure.Dashboard;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public sealed class ConcurrentOperationsTests
{
    private readonly SimulationService _sut = new(TestDatabase.ConnectionString);

    [Fact]
    public async Task RunChangeOfSupplier_single_action_completes()
    {
        var steps = new List<SimulationStep>();
        await _sut.RunChangeOfSupplierAsync(
            "571313100000099901", "Test Customer 1",
            step => { steps.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        steps.Should().HaveCount(8);
        steps.Last().Name.Should().Be("Run Settlement");
        steps.Last().Details.Should().Contain("DKK");
    }

    [Fact]
    public async Task RunChangeOfSupplier_two_concurrent_actions_complete()
    {
        var steps1 = new List<SimulationStep>();
        var steps2 = new List<SimulationStep>();

        var task1 = _sut.RunChangeOfSupplierAsync(
            "571313100000099801", "Concurrent A",
            step => { steps1.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        var task2 = _sut.RunChangeOfSupplierAsync(
            "571313100000099802", "Concurrent B",
            step => { steps2.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        await Task.WhenAll(task1, task2);

        steps1.Should().HaveCount(8);
        steps2.Should().HaveCount(8);
    }

    [Fact]
    public async Task RunChangeOfSupplier_same_gsrn_twice_succeeds_idempotently()
    {
        var steps1 = new List<SimulationStep>();
        await _sut.RunChangeOfSupplierAsync(
            "571313100000099701", "Run 1",
            step => { steps1.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        var steps2 = new List<SimulationStep>();
        await _sut.RunChangeOfSupplierAsync(
            "571313100000099701", "Run 2",
            step => { steps2.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        steps1.Should().HaveCount(8);
        steps2.Should().HaveCount(8);
    }
}
