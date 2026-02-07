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

    [Fact]
    public async Task ReceiveMetering_after_onboard_completes()
    {
        const string gsrn = "571313100000098801";

        // First: onboard
        await _sut.RunChangeOfSupplierAsync(
            gsrn, "Metering Test",
            _ => Task.CompletedTask,
            CancellationToken.None);

        // Then: receive metering
        var steps = new List<SimulationStep>();
        await _sut.ReceiveMeteringOpsAsync(
            gsrn,
            step => { steps.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        steps.Should().HaveCount(2);
        steps[0].Name.Should().Be("Seed Spot Prices");
        steps[1].Name.Should().Be("Receive RSM-012");
        steps[1].Details.Should().Contain("kWh");
    }

    [Fact]
    public async Task RunSettlement_after_receiving_metering_completes()
    {
        const string gsrn = "571313100000098802";

        // Onboard
        await _sut.RunChangeOfSupplierAsync(
            gsrn, "Settlement Test",
            _ => Task.CompletedTask,
            CancellationToken.None);

        // Receive more metering data
        await _sut.ReceiveMeteringOpsAsync(
            gsrn,
            _ => Task.CompletedTask,
            CancellationToken.None);

        // Run settlement on the new data
        var steps = new List<SimulationStep>();
        await _sut.RunSettlementOpsAsync(
            gsrn,
            step => { steps.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        steps.Should().HaveCount(3);
        steps[0].Name.Should().Be("Load Data");
        steps[1].Name.Should().Be("Calculate Settlement");
        steps[2].Name.Should().Be("Store Settlement");
        steps[2].Details.Should().Contain("DKK");
    }

    [Fact]
    public async Task Offboard_after_onboard_completes()
    {
        const string gsrn = "571313100000098803";

        // Onboard
        await _sut.RunChangeOfSupplierAsync(
            gsrn, "Offboard Test",
            _ => Task.CompletedTask,
            CancellationToken.None);

        // Offboard
        var steps = new List<SimulationStep>();
        await _sut.OffboardOpsAsync(
            gsrn,
            step => { steps.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        steps.Should().HaveCount(5);
        steps[0].Name.Should().Be("Start Offboarding");
        steps[1].Name.Should().Be("Final Metering Data");
        steps[2].Name.Should().Be("Final Settlement");
        steps[2].Details.Should().Contain("DKK");
        steps[3].Name.Should().Be("Deactivate");
        steps[4].Name.Should().Be("Final Settled");
    }

    [Fact]
    public async Task AcontoBilling_after_onboard_completes()
    {
        const string gsrn = "571313100000098804";

        // Onboard
        await _sut.RunChangeOfSupplierAsync(
            gsrn, "Aconto Test",
            _ => Task.CompletedTask,
            CancellationToken.None);

        // Aconto billing
        var steps = new List<SimulationStep>();
        await _sut.AcontoBillingOpsAsync(
            gsrn,
            step => { steps.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        steps.Should().HaveCount(4);
        steps[0].Name.Should().Be("Estimate Aconto");
        steps[0].Details.Should().Contain("DKK");
        steps[1].Name.Should().Be("Record Payment");
        steps[2].Name.Should().Be("Receive Metering");
        steps[3].Name.Should().Be("Reconcile");
        steps[3].Details.Should().Contain("difference");
    }
}
