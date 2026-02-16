using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

/// <summary>
/// Tests the standardized billing model where aconto is just line items on an invoice.
/// Settlement engine produces the actual charges; aconto deduction/prepayment are arithmetic.
/// </summary>
public class AcontoBillingTests
{
    private static readonly SettlementEngine Engine = new();

    private static SettlementRequest BuildJanuaryRequest()
    {
        var consumption = new List<MeteringDataRow>();
        var spotPrices = new List<SpotPriceRow>();
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 744; i++)
        {
            var ts = start.AddHours(i);
            var hour = ts.Hour;
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
            consumption.Add(new MeteringDataRow(ts, "PT1H", kwh, "A03", "test"));
            spotPrices.Add(new SpotPriceRow("DK1", ts, spot));
        }

        var gridRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
        {
            >= 1 and <= 6 => 0.06m,
            >= 7 and <= 16 => 0.18m,
            >= 17 and <= 20 => 0.54m,
            _ => 0.06m,
        })).ToList();

        return new SettlementRequest(
            "571313100000012345",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            consumption, spotPrices, gridRates,
            0.054m, 0.049m, 0.008m,
            49.00m, 0.04m, 0m, 39.00m);
    }

    [Fact]
    public void Settlement_total_is_invariant_regardless_of_billing_model()
    {
        // The settlement engine always produces the same result — aconto is a billing concern, not settlement
        var request = BuildJanuaryRequest();
        var result = Engine.Calculate(request);

        result.Total.Should().Be(793.14m, "golden master total for January 2025");
    }

    [Fact]
    public void Underpayment_aconto_deduction_leaves_positive_net()
    {
        var request = BuildJanuaryRequest();
        var settlement = Engine.Calculate(request);

        // Standard billing model: difference = actual - prepaid, net = difference + new estimate
        var acontoPaid = 700.00m;
        var difference = settlement.Total - acontoPaid;
        var newEstimate = 800.00m;
        var totalDue = difference + newEstimate;

        difference.Should().Be(93.14m, "underpaid by 93.14");
        totalDue.Should().Be(893.14m, "settlement difference + new aconto estimate");
    }

    [Fact]
    public void Overpayment_aconto_deduction_reduces_net()
    {
        var request = BuildJanuaryRequest();
        var settlement = Engine.Calculate(request);

        var acontoPaid = 900.00m;
        var difference = settlement.Total - acontoPaid;
        var newEstimate = 800.00m;
        var totalDue = difference + newEstimate;

        difference.Should().Be(-106.86m, "overpaid by 106.86");
        totalDue.Should().Be(693.14m, "negative difference reduces next quarter charge");
    }

    [Fact]
    public void Exact_payment_produces_zero_difference()
    {
        var request = BuildJanuaryRequest();
        var settlement = Engine.Calculate(request);

        var acontoPaid = 793.14m;
        var difference = settlement.Total - acontoPaid;
        var newEstimate = 800.00m;
        var totalDue = difference + newEstimate;

        difference.Should().Be(0m);
        totalDue.Should().Be(800.00m, "only the new estimate is due");
    }

    [Fact]
    public void Aconto_estimator_produces_reasonable_quarterly_amount()
    {
        var quarterly = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: 4000m,
            expectedPricePerKwh: AcontoEstimator.CalculateExpectedPricePerKwh(
                averageSpotPriceOrePerKwh: 80m,
                marginOrePerKwh: 4m,
                systemTariffRate: 0.054m,
                transmissionTariffRate: 0.049m,
                electricityTaxRate: 0.008m,
                averageGridTariffRate: 0.20m));

        quarterly.Should().BeGreaterThan(0, "estimate should be positive");
        quarterly.Should().BeLessThan(5000m, "should be a reasonable amount");
    }
}
