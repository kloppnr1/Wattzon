using DataHub.Settlement.Application.Billing;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class AcontoEstimatorTests
{
    [Fact]
    public void Quarterly_estimate_from_annual_consumption()
    {
        // 4800 kWh/year → quarterly = 1200 kWh
        var result = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: 4800m, expectedPricePerKwh: 1.50m);

        // 1200 kWh × 1.50 DKK/kWh = 1800.00, +25% VAT = 2250.00
        result.Should().Be(2250.00m);
    }

    [Fact]
    public void Quarterly_estimate_for_typical_house()
    {
        // Default Danish house: 4000 kWh/year → quarterly = 1000 kWh
        var result = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: 4000m, expectedPricePerKwh: 1.50m);

        // 1000 kWh × 1.50 = 1500.00, +25% VAT = 1875.00
        result.Should().Be(1875.00m);
    }

    [Fact]
    public void Quarterly_estimate_for_apartment()
    {
        // Typical apartment: 2500 kWh/year → quarterly = 625 kWh
        var result = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: 2500m, expectedPricePerKwh: 1.50m);

        // 625 kWh × 1.50 = 937.50, +25% VAT = 1171.875 → 1171.88
        result.Should().Be(1171.88m);
    }

    [Fact]
    public void Quarterly_estimate_includes_subscriptions()
    {
        // 4800 kWh/year → quarterly = 1200 kWh
        var result = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: 4800m, expectedPricePerKwh: 1.50m,
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
