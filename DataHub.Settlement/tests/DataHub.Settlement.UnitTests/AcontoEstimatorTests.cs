using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Eloverblik;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class AcontoEstimatorTests
{
    [Fact]
    public void Quarterly_estimate_from_12_month_history()
    {
        // 12 months × 400 kWh = 4800 kWh/year, monthly avg = 400, quarterly = 1200 kWh
        var history = Enumerable.Range(1, 12)
            .Select(m => new MonthlyConsumption(2024, m, 400m))
            .ToList();

        var result = AcontoEstimator.EstimateQuarterlyAmount(history, expectedPricePerKwh: 1.50m);

        // 1200 kWh × 1.50 DKK/kWh = 1800.00, +25% VAT = 2250.00
        result.Should().Be(2250.00m);
    }

    [Fact]
    public void Quarterly_estimate_with_seasonal_variation()
    {
        // Winter-heavy: 3 months at 400 kWh average
        var history = new List<MonthlyConsumption>
        {
            new(2024, 1, 420m),
            new(2024, 2, 380m),
            new(2024, 3, 350m),
        };

        var result = AcontoEstimator.EstimateQuarterlyAmount(history, expectedPricePerKwh: 1.50m);

        // Average monthly = (420+380+350)/3 = 383.33, quarterly = 1150 kWh
        // 1150 × 1.50 = 1725, +25% VAT = 2156.25
        var expectedMonthlyAvg = (420m + 380m + 350m) / 3m;
        var expectedQuarterlyKwh = expectedMonthlyAvg * 3m;
        var expected = Math.Round(expectedQuarterlyKwh * 1.50m * 1.25m, 2);
        result.Should().Be(expected);
    }

    [Fact]
    public void Empty_history_returns_zero()
    {
        var result = AcontoEstimator.EstimateQuarterlyAmount([], expectedPricePerKwh: 1.50m);

        result.Should().Be(0m);
    }

    [Fact]
    public void Quarterly_estimate_includes_subscriptions()
    {
        // 12 months × 400 kWh, quarterly = 1200 kWh
        var history = Enumerable.Range(1, 12)
            .Select(m => new MonthlyConsumption(2024, m, 400m))
            .ToList();

        var result = AcontoEstimator.EstimateQuarterlyAmount(
            history, expectedPricePerKwh: 1.50m,
            gridSubscriptionPerMonth: 49.00m,
            supplierSubscriptionPerMonth: 39.00m);

        // Variable: 1200 kWh × 1.50 = 1800.00
        // Subscriptions: (49 + 39) × 3 = 264.00
        // Subtotal: 2064.00, +25% VAT = 2580.00
        result.Should().Be(2580.00m);
    }

    [Fact]
    public void Expected_price_per_kwh_calculation()
    {
        // Spot 80 øre, margin 4 øre, system 0.054, transmission 0.049, tax 0.008, grid avg 0.20
        var result = AcontoEstimator.CalculateExpectedPricePerKwh(
            averageSpotPriceOrePerKwh: 80m,
            marginOrePerKwh: 4m,
            systemTariffRate: 0.054m,
            transmissionTariffRate: 0.049m,
            electricityTaxRate: 0.008m,
            averageGridTariffRate: 0.20m);

        // spot=0.80 + margin=0.04 + system=0.054 + transmission=0.049 + tax=0.008 + grid=0.20 = 1.151
        result.Should().Be(1.151m);
    }
}
