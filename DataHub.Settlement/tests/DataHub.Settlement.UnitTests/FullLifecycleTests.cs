using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

/// <summary>
/// End-to-end lifecycle tests validating the full flow using in-memory repositories.
/// </summary>
public class FullLifecycleTests
{
    private readonly ProcessStateMachineTests.InMemoryProcessRepository _processRepo = new();

    private static SettlementRequest BuildMonthRequest(DateOnly start, DateOnly end)
    {
        var consumption = new List<MeteringDataRow>();
        var spotPrices = new List<SpotPriceRow>();
        var current = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endDt = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        while (current < endDt)
        {
            var hour = current.Hour;
            var kwh = hour switch
            {
                >= 0 and <= 5 => 0.300m,
                >= 6 and <= 15 => 0.500m,
                >= 16 and <= 19 => 1.200m,
                _ => 0.400m,
            };
            var spot = hour switch
            {
                >= 0 and <= 5 => 45m,
                >= 6 and <= 15 => 85m,
                >= 16 and <= 19 => 125m,
                _ => 55m,
            };
            consumption.Add(new MeteringDataRow(current, "PT1H", kwh, "A03", "test"));
            spotPrices.Add(new SpotPriceRow("DK1", current, spot));
            current = current.AddHours(1);
        }

        var gridRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
        {
            >= 1 and <= 6 => 0.06m,
            >= 7 and <= 16 => 0.18m,
            >= 17 and <= 20 => 0.54m,
            _ => 0.06m,
        })).ToList();

        return new SettlementRequest(
            "571313100000012345", start, end,
            consumption, spotPrices, gridRates,
            0.054m, 0.049m, 0.008m,
            49.00m, 0.04m, 0m, 39.00m);
    }

    [Fact]
    public async Task Full_lifecycle_onboarding_to_offboarding()
    {
        var sm = new ProcessStateMachine(_processRepo);
        var engine = new SettlementEngine();

        // 1. Create and send BRS-001
        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-lifecycle-001", CancellationToken.None);

        // 2. DataHub acknowledges → effectuation_pending
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        var state = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        state!.Status.Should().Be("effectuation_pending");

        // 3. Complete process (RSM-007 received)
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        // 4. Run January settlement
        var janRequest = BuildMonthRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        var janResult = engine.Calculate(janRequest);
        janResult.Total.Should().Be(793.14m);

        // 5. Offboarding starts (another supplier takes over)
        await sm.MarkOffboardingAsync(request.Id, CancellationToken.None);
        state = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        state!.Status.Should().Be("offboarding");

        // 6. Final settlement (partial Feb)
        var finalService = new FinalSettlementService(engine);
        var febRequest = BuildMonthRequest(new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 16));
        var finalResult = finalService.CalculateFinal(febRequest, acontoPaid: null);
        finalResult.TotalDue.Should().BeGreaterThan(0);

        // 7. Mark final settled
        await sm.MarkFinalSettledAsync(request.Id, CancellationToken.None);
        state = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        state!.Status.Should().Be("final_settled");
    }

    [Fact]
    public async Task Rejection_and_retry()
    {
        var sm = new ProcessStateMachine(_processRepo);

        // 1. Submit BRS-001
        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-reject-001", CancellationToken.None);

        // 2. DataHub rejects
        await sm.MarkRejectedAsync(request.Id, "E16: Invalid GSRN checksum", CancellationToken.None);
        var state = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        state!.Status.Should().Be("rejected");

        // 3. Retry with new request
        var retry = await sm.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(retry.Id, "corr-retry-001", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(retry.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(retry.Id, CancellationToken.None);

        var retryState = await _processRepo.GetAsync(retry.Id, CancellationToken.None);
        retryState!.Status.Should().Be("completed");
    }

    [Fact]
    public async Task Aconto_quarterly_settlement()
    {
        var sm = new ProcessStateMachine(_processRepo);
        var engine = new SettlementEngine();
        var acontoService = new AcontoSettlementService(engine);

        // 1. Complete onboarding
        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-aconto-001", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        // 2. Run Q1 settlement with aconto reconciliation
        var janRequest = BuildMonthRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));

        // Customer paid 700 DKK aconto for January
        var result = acontoService.CalculateQuarterlyInvoice(
            janRequest,
            totalAcontoPaid: 700.00m,
            newQuarterlyEstimate: 800.00m);

        // Actual is 793.14, paid 700, difference = 93.14
        result.PreviousQuarter.ActualSettlement.Total.Should().Be(793.14m);
        result.PreviousQuarter.Difference.Should().Be(93.14m);
        result.TotalDue.Should().Be(893.14m); // 93.14 underpayment + 800.00 next quarter
    }

    [Fact]
    public async Task Move_in_lifecycle()
    {
        var sm = new ProcessStateMachine(_processRepo);
        var engine = new SettlementEngine();

        // 1. Create and send BRS-009 (move in)
        var request = await sm.CreateRequestAsync("571313100000012345", "move_in",
            new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-movein-001", CancellationToken.None);

        // 2. DataHub acknowledges → effectuation_pending
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        var state = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        state!.Status.Should().Be("effectuation_pending");

        // 3. Complete process (RSM-007 received)
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        // 4. Run January settlement
        var janRequest = BuildMonthRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        var janResult = engine.Calculate(janRequest);
        janResult.Total.Should().Be(793.14m);

        // 5. Mark final settled (move-in complete lifecycle)
        await sm.MarkOffboardingAsync(request.Id, CancellationToken.None);
        await sm.MarkFinalSettledAsync(request.Id, CancellationToken.None);
        state = await _processRepo.GetAsync(request.Id, CancellationToken.None);
        state!.Status.Should().Be("final_settled");
    }

    [Fact]
    public async Task Move_out_lifecycle()
    {
        var sm = new ProcessStateMachine(_processRepo);
        var engine = new SettlementEngine();

        // 1. Establish supply via supplier_switch
        var onboard = await sm.CreateRequestAsync("571313100000012345", "supplier_switch",
            new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(onboard.Id, "corr-moveout-setup", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(onboard.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(onboard.Id, CancellationToken.None);

        // 2. Run January settlement
        var janRequest = BuildMonthRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        var janResult = engine.Calculate(janRequest);
        janResult.Total.Should().Be(793.14m);

        // 3. Create move_out process (BRS-010)
        var moveOut = await sm.CreateRequestAsync("571313100000012345", "move_out",
            new DateOnly(2025, 2, 16), CancellationToken.None);
        await sm.MarkSentAsync(moveOut.Id, "corr-moveout-001", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(moveOut.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(moveOut.Id, CancellationToken.None);

        // 4. Final settlement (partial Feb)
        var finalService = new FinalSettlementService(engine);
        var febRequest = BuildMonthRequest(new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 16));
        var finalResult = finalService.CalculateFinal(febRequest, acontoPaid: null);
        finalResult.TotalDue.Should().BeGreaterThan(0);

        // 5. Offboard and final settle the move_out process
        await sm.MarkOffboardingAsync(moveOut.Id, CancellationToken.None);
        await sm.MarkFinalSettledAsync(moveOut.Id, CancellationToken.None);
        var state = await _processRepo.GetAsync(moveOut.Id, CancellationToken.None);
        state!.Status.Should().Be("final_settled");
    }
}
