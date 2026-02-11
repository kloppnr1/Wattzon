using DataHub.Settlement.Infrastructure.Dashboard;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public sealed class AcontoChangeOfSupplierIntegrationTests
{
    private readonly TestClock _clock = new() { Today = new DateOnly(2024, 12, 20) };

    private SimulationService CreateService() =>
        new(TestDatabase.ConnectionString, _clock);

    [Fact]
    public async Task Full_aconto_cos_tick_flow_completes_all_10_steps()
    {
        var sut = CreateService();
        var effectiveDate = new DateOnly(2025, 1, 1);
        var ctx = new AcontoChangeOfSupplierContext("571313100000097001", "Aconto Tick Test", effectiveDate);
        var allSteps = new List<SimulationStep>();

        // Advance clock day by day through the entire timeline until settled
        var maxDate = effectiveDate.AddDays(40); // safety limit
        while (!ctx.IsAcontoSettled && _clock.Today < maxDate)
        {
            var executed = await sut.TickAcontoChangeOfSupplierAsync(ctx, _clock.Today, CancellationToken.None);
            allSteps.AddRange(executed);
            _clock.Today = _clock.Today.AddDays(1);
        }

        ctx.IsSeeded.Should().BeTrue();
        ctx.IsBrsSubmitted.Should().BeTrue();
        ctx.IsAcknowledged.Should().BeTrue();
        ctx.IsAcontoEstimated.Should().BeTrue();
        ctx.IsInvoiceSent.Should().BeTrue();
        ctx.IsRsm022Received.Should().BeTrue();
        ctx.IsEffectuated.Should().BeTrue();
        ctx.IsAcontoPaid.Should().BeTrue();
        ctx.IsAcontoSettled.Should().BeTrue();
        ctx.AcontoEstimate.Should().BeGreaterThan(0);

        // Verify step names in order
        var stepNames = allSteps.Select(s => s.Name).ToList();
        stepNames.Should().StartWith(new[]
        {
            "Seed Data", "Submit BRS-001", "DataHub Acknowledges",
            "Estimate Aconto", "Send Invoice", "Receive RSM-022", "Effectuation"
        });
        // RSM-012 deliveries happen between effectuation and payment
        stepNames.Should().Contain("Receive RSM-012");
        stepNames.Should().Contain("Record Payment");
        stepNames.Should().Contain("Aconto Settlement");

        // Settlement step should contain DKK
        var settlementStep = allSteps.Last(s => s.Name == "Aconto Settlement");
        settlementStep.Details.Should().Contain("DKK");
    }

    [Fact]
    public async Task Aconto_cos_followed_by_move_out_completes_without_error()
    {
        var sut = CreateService();
        var effectiveDate = new DateOnly(2025, 1, 1);
        const string gsrn = "571313100000097002";
        var ctx = new AcontoChangeOfSupplierContext(gsrn, "Aconto MoveOut Test", effectiveDate);

        // Tick through until aconto settled
        var maxDate = effectiveDate.AddDays(40);
        while (!ctx.IsAcontoSettled && _clock.Today < maxDate)
        {
            await sut.TickAcontoChangeOfSupplierAsync(ctx, _clock.Today, CancellationToken.None);
            _clock.Today = _clock.Today.AddDays(1);
        }
        ctx.IsAcontoSettled.Should().BeTrue();

        // Advance clock past the hard-coded move-out effective date (2025-02-16)
        _clock.Today = new DateOnly(2025, 3, 1);

        // Now run Move Out — this previously crashed with NULL metering error
        var moveOutSteps = new List<SimulationStep>();
        await sut.RunMoveOutAsync(
            gsrn,
            step => { moveOutSteps.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        moveOutSteps.Should().HaveCount(6);
        moveOutSteps[0].Name.Should().Be("Submit BRS-010");
        moveOutSteps[1].Name.Should().Be("Final Metering Data");
        moveOutSteps[2].Name.Should().Be("Final Settlement");
        moveOutSteps[2].Details.Should().Contain("DKK");
        moveOutSteps[3].Name.Should().Be("Deactivate");
    }

    [Fact]
    public async Task Aconto_cos_followed_by_offboard_completes_without_error()
    {
        var sut = CreateService();
        var effectiveDate = new DateOnly(2025, 1, 1);
        const string gsrn = "571313100000097003";
        var ctx = new AcontoChangeOfSupplierContext(gsrn, "Aconto Offboard Test", effectiveDate);

        // Tick through until aconto settled
        var maxDate = effectiveDate.AddDays(40);
        while (!ctx.IsAcontoSettled && _clock.Today < maxDate)
        {
            await sut.TickAcontoChangeOfSupplierAsync(ctx, _clock.Today, CancellationToken.None);
            _clock.Today = _clock.Today.AddDays(1);
        }
        ctx.IsAcontoSettled.Should().BeTrue();

        // Now run Offboard — this previously crashed with NULL metering error
        var offboardSteps = new List<SimulationStep>();
        await sut.OffboardOpsAsync(
            gsrn,
            step => { offboardSteps.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        offboardSteps.Should().HaveCount(5);
        offboardSteps[0].Name.Should().Be("Start Offboarding");
        offboardSteps[1].Name.Should().Be("Final Metering Data");
        offboardSteps[2].Name.Should().Be("Final Settlement");
        offboardSteps[2].Details.Should().Contain("DKK");
        offboardSteps[3].Name.Should().Be("Deactivate");
        offboardSteps[4].Name.Should().Be("Final Settled");
    }

    [Fact]
    public async Task Aconto_estimate_is_persisted_in_settlement_tables()
    {
        var sut = CreateService();
        var effectiveDate = new DateOnly(2025, 1, 1);
        const string gsrn = "571313100000097004";
        var ctx = new AcontoChangeOfSupplierContext(gsrn, "Aconto Persist Test", effectiveDate);

        // Tick all the way through
        var maxDate = effectiveDate.AddDays(40);
        while (!ctx.IsAcontoSettled && _clock.Today < maxDate)
        {
            await sut.TickAcontoChangeOfSupplierAsync(ctx, _clock.Today, CancellationToken.None);
            _clock.Today = _clock.Today.AddDays(1);
        }

        // Verify data is in DB via the summary
        var summary = await sut.GetMeteringPointSummaryAsync(gsrn, CancellationToken.None);
        summary.Should().NotBeNull();
        summary!.Settlements.Should().HaveCount(1);
        summary.Settlements[0].TotalAmount.Should().Be(ctx.AcontoEstimate);
        summary.AcontoPayments.Should().HaveCount(1);
        summary.AcontoPayments[0].Amount.Should().Be(ctx.AcontoEstimate);
    }
}
