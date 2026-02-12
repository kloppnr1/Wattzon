using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Infrastructure.Dashboard;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class AcontoChangeOfSupplierTests
{
    private static readonly DateOnly Jan1 = new(2025, 1, 1);

    [Fact]
    public void Estimate_is_calculated_correctly_with_standard_inputs()
    {
        // Same parameters as used in TickAcontoChangeOfSupplierAsync
        var expectedPrice = AcontoEstimator.CalculateExpectedPricePerKwh(
            averageSpotPriceOrePerKwh: 75m, marginOrePerKwh: 4.0m,
            systemTariffRate: 0.054m, transmissionTariffRate: 0.049m,
            electricityTaxRate: 0.008m, averageGridTariffRate: 0.18m);

        // spot=0.75 + margin=0.04 + 0.054 + 0.049 + 0.008 + 0.18 = 1.081
        expectedPrice.Should().Be(1.081m);

        var quarterly = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: 4000m, expectedPrice,
            gridSubscriptionPerMonth: 49.00m, supplierSubscriptionPerMonth: 39.00m);

        // Variable: 1000 kWh × 1.081 = 1081.00
        // Subscriptions: (49 + 39) × 3 = 264.00
        // Subtotal: 1345.00, +25% VAT = 1681.25
        quarterly.Should().Be(1681.25m);
    }

    [Fact]
    public void Invoice_sent_after_effectuation_in_timeline()
    {
        var timeline = DataHubTimeline.BuildAcontoChangeOfSupplierTimeline(Jan1);

        var invoiceDate = timeline.GetDate("Send Invoice");
        var effectuationDate = timeline.GetDate("Effectuation");

        invoiceDate.Should().NotBeNull();
        effectuationDate.Should().NotBeNull();
        invoiceDate!.Value.Should().BeAfter(effectuationDate!.Value);
    }

    [Fact]
    public void Payment_is_after_effectuation_in_timeline()
    {
        var timeline = DataHubTimeline.BuildAcontoChangeOfSupplierTimeline(Jan1);

        var paymentDate = timeline.GetDate("Record Payment");
        var effectuationDate = timeline.GetDate("Effectuation");

        paymentDate.Should().NotBeNull();
        effectuationDate.Should().NotBeNull();
        paymentDate!.Value.Should().BeAfter(effectuationDate!.Value);
    }

    [Fact]
    public void Aconto_settlement_runs_at_payment_date()
    {
        var timeline = DataHubTimeline.BuildAcontoChangeOfSupplierTimeline(Jan1);

        var paymentDate = timeline.GetDate("Record Payment");
        var settlementDate = timeline.GetDate("Aconto Settlement");

        paymentDate.Should().NotBeNull();
        settlementDate.Should().NotBeNull();
        settlementDate!.Value.Should().Be(paymentDate!.Value);
    }

    [Fact]
    public void Timeline_has_10_events()
    {
        var timeline = DataHubTimeline.BuildAcontoChangeOfSupplierTimeline(Jan1);
        timeline.Events.Should().HaveCount(10);
    }

    [Fact]
    public void Context_TotalMeteringDays_is_31_for_January()
    {
        var ctx = new AcontoChangeOfSupplierContext("571313100000000001", "Test", Jan1);
        ctx.TotalMeteringDays.Should().Be(31);
    }

    [Fact]
    public void Estimate_date_is_D_plus_1()
    {
        var timeline = DataHubTimeline.BuildAcontoChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("Estimate Aconto").Should().Be(new DateOnly(2025, 1, 2));
    }

    [Fact]
    public void Invoice_date_is_D_plus_2()
    {
        var timeline = DataHubTimeline.BuildAcontoChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("Send Invoice").Should().Be(new DateOnly(2025, 1, 3));
    }

    [Fact]
    public void Payment_date_is_D_plus_14()
    {
        var timeline = DataHubTimeline.BuildAcontoChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("Record Payment").Should().Be(new DateOnly(2025, 1, 15));
    }
}
