using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

/// <summary>
/// Hand-calculated reference invoices that the settlement engine must reproduce exactly.
///
/// Consumption per day (24 hours, same pattern every day):
///   Hours 0-5 (night):      0.300 kWh each
///   Hours 6-15 (day):       0.500 kWh each
///   Hours 16-19 (peak):     1.200 kWh each
///   Hours 20-23 (late):     0.400 kWh each
///   Daily total: 13.200 kWh
///
/// Spot prices (same every day):
///   Night: 0.45, Day: 0.85, Peak: 1.25, Late: 0.55 DKK/kWh
///
/// Tariffs:
///   Grid: night 0.06, day 0.18, peak 0.54 DKK/kWh
///   System: 0.054, Transmission: 0.049, Elafgift: 0.008 DKK/kWh
///   Grid subscription: 49.00 DKK/month
///
/// Product: margin 4 øre = 0.04 DKK/kWh, supplier subscription 39.00 DKK/month
/// </summary>
public class GoldenMasterTests
{
    private const string Gsrn = "571313100000012345";
    private const string PriceArea = "DK1";
    private const decimal MarginPerKwh = 0.04m;
    private const decimal SystemRate = 0.054m;
    private const decimal TransmissionRate = 0.049m;
    private const decimal ElectricityTaxRate = 0.008m;
    private const decimal GridSubscription = 49.00m;
    private const decimal SupplierSubscription = 39.00m;

    private static readonly SettlementEngine Engine = new();

    private static decimal SpotPriceForHour(int hour) => hour switch
    {
        >= 0 and <= 5 => 0.45m,
        >= 6 and <= 15 => 0.85m,
        >= 16 and <= 19 => 1.25m,
        _ => 0.55m, // 20-23
    };

    private static decimal ConsumptionForHour(int hour) => hour switch
    {
        >= 0 and <= 5 => 0.300m,
        >= 6 and <= 15 => 0.500m,
        >= 16 and <= 19 => 1.200m,
        _ => 0.400m, // 20-23
    };

    private static IReadOnlyList<TariffRateRow> GridTariffRates()
    {
        var rates = new List<TariffRateRow>();
        for (var h = 1; h <= 24; h++)
        {
            var rate = h switch
            {
                >= 1 and <= 6 => 0.06m,    // night (00-05)
                >= 7 and <= 16 => 0.18m,   // day (06-15)
                >= 17 and <= 20 => 0.54m,  // peak (16-19)
                _ => 0.06m,                // night (20-23)
            };
            rates.Add(new TariffRateRow(h, rate));
        }
        return rates;
    }

    private static SettlementRequest BuildRequest(DateOnly periodStart, DateOnly periodEnd)
    {
        var consumption = new List<MeteringDataRow>();
        var spotPrices = new List<SpotPriceRow>();

        var current = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        while (current < end)
        {
            var hour = current.Hour;
            consumption.Add(new MeteringDataRow(current, "PT1H", ConsumptionForHour(hour), "A03", "golden-master"));
            spotPrices.Add(new SpotPriceRow(PriceArea, current, SpotPriceForHour(hour)));
            current = current.AddHours(1);
        }

        return new SettlementRequest(
            Gsrn, periodStart, periodEnd,
            consumption, spotPrices, GridTariffRates(),
            SystemRate, TransmissionRate, ElectricityTaxRate,
            GridSubscription, MarginPerKwh, 0m, SupplierSubscription);
    }

    /// <summary>
    /// Golden Master #1: Full January 2025 (31 days, 744 hours, 409.200 kWh)
    ///
    /// Energy:             31 × (6×0.300×0.49 + 10×0.500×0.89 + 4×1.200×1.29 + 4×0.400×0.59) = 31 × 12.468 = 386.508 → 386.51
    /// Grid tariff:        31 × (6×0.300×0.06 + 10×0.500×0.18 + 4×1.200×0.54 + 4×0.400×0.06) = 31 × 3.696  = 114.576 → 114.58
    /// System tariff:      409.200 × 0.054 = 22.0968 → 22.10
    /// Transmission:       409.200 × 0.049 = 20.0508 → 20.05
    /// Electricity tax:    409.200 × 0.008 = 3.2736  → 3.27
    /// Grid subscription:  49.00
    /// Supplier sub:       39.00
    /// Subtotal:           634.51
    /// VAT (25%):          158.63
    /// Total:              793.14
    /// </summary>
    [Fact]
    public void Full_january_matches_golden_master()
    {
        var request = BuildRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        var result = Engine.Calculate(request);

        result.TotalKwh.Should().Be(409.200m);
        result.Lines.Should().HaveCount(7);

        AssertLine(result, "energy", 386.51m);
        AssertLine(result, "grid_tariff", 114.58m);
        AssertLine(result, "system_tariff", 22.10m);
        AssertLine(result, "transmission_tariff", 20.05m);
        AssertLine(result, "electricity_tax", 3.27m);
        AssertLine(result, "grid_subscription", 49.00m);
        AssertLine(result, "supplier_subscription", 39.00m);

        result.Subtotal.Should().Be(634.51m);
        result.VatAmount.Should().Be(158.63m);
        result.Total.Should().Be(793.14m);
    }

    /// <summary>
    /// Golden Master #2: Partial January (Jan 16-31, 16 days, 384 hours, 211.200 kWh)
    ///
    /// Energy:             16 × 12.468 = 199.488 → 199.49
    /// Grid tariff:        16 × 3.696  = 59.136  → 59.14
    /// System tariff:      211.200 × 0.054 = 11.4048 → 11.40
    /// Transmission:       211.200 × 0.049 = 10.3488 → 10.35
    /// Electricity tax:    211.200 × 0.008 = 1.6896  → 1.69
    /// Grid sub (pro rata): 49.00 × 16/31 = 25.29
    /// Supplier sub:       39.00 × 16/31 = 20.13
    /// Subtotal:           327.49
    /// VAT (25%):          81.87
    /// Total:              409.36
    /// </summary>
    [Fact]
    public void Partial_january_matches_golden_master()
    {
        var request = BuildRequest(new DateOnly(2025, 1, 16), new DateOnly(2025, 2, 1));
        var result = Engine.Calculate(request);

        result.TotalKwh.Should().Be(211.200m);

        AssertLine(result, "energy", 199.49m);
        AssertLine(result, "grid_tariff", 59.14m);
        AssertLine(result, "system_tariff", 11.40m);
        AssertLine(result, "transmission_tariff", 10.35m);
        AssertLine(result, "electricity_tax", 1.69m);
        AssertLine(result, "grid_subscription", 25.29m);
        AssertLine(result, "supplier_subscription", 20.13m);

        result.Subtotal.Should().Be(327.49m);
        result.VatAmount.Should().Be(81.87m);
        result.Total.Should().Be(409.36m);
    }

    private static void AssertLine(SettlementResult result, string chargeType, decimal expectedAmount)
    {
        var line = result.Lines.Single(l => l.ChargeType == chargeType);
        line.Amount.Should().Be(expectedAmount, $"{chargeType} should be {expectedAmount}");
    }
}
