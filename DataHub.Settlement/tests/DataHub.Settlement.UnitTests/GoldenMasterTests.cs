using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using FluentAssertions.Execution;
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
/// Spot prices (same every day, in øre/kWh as delivered by DDQ):
///   Night: 45, Day: 85, Peak: 125, Late: 55 øre/kWh
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
        >= 0 and <= 5 => 45m,
        >= 6 and <= 15 => 85m,
        >= 16 and <= 19 => 125m,
        _ => 55m, // 20-23
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

    /// <summary>
    /// Golden Master #3: Aconto quarterly settlement (Jan 2025)
    ///
    /// Actual settlement identical to GM#1: 793.14 DKK
    /// Aconto paid for January: 700.00 DKK
    /// Difference (underpayment): 93.14 DKK
    /// New quarterly estimate: 800.00 DKK
    /// Total due on combined invoice: 893.14 DKK
    /// </summary>
    [Fact]
    public void Aconto_quarterly_settlement_matches_golden_master()
    {
        var request = BuildRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        var acontoService = new AcontoSettlementService(Engine);

        var result = acontoService.CalculateQuarterlyInvoice(
            request, totalAcontoPaid: 700.00m, newQuarterlyEstimate: 800.00m);

        result.PreviousQuarter.ActualSettlement.Total.Should().Be(793.14m);
        result.PreviousQuarter.TotalAcontoPaid.Should().Be(700.00m);
        result.PreviousQuarter.Difference.Should().Be(93.14m);
        result.PreviousQuarter.NewQuarterlyEstimate.Should().Be(800.00m);
        result.NewAcontoAmount.Should().Be(800.00m);
        result.TotalDue.Should().Be(893.14m);
    }

    /// <summary>
    /// Golden Master #4: Final settlement at offboarding (Jan 16 - Feb 1)
    ///
    /// Actual settlement identical to GM#2: 409.36 DKK
    /// Aconto paid (pro-rata): 300.00 DKK
    /// Difference: 109.36 DKK
    /// Total due: 109.36 DKK (only the difference, no next-quarter aconto)
    /// </summary>
    [Fact]
    public void Final_settlement_at_offboarding_matches_golden_master()
    {
        var request = BuildRequest(new DateOnly(2025, 1, 16), new DateOnly(2025, 2, 1));
        var finalService = new FinalSettlementService(Engine);

        var result = finalService.CalculateFinal(request, acontoPaid: 300.00m);

        result.Settlement.Total.Should().Be(409.36m);
        result.AcontoPaid.Should().Be(300.00m);
        result.AcontoDifference.Should().Be(109.36m);
        result.TotalDue.Should().Be(109.36m);
    }

    /// <summary>
    /// Golden Master #5: Correction delta settlement
    ///
    /// 3 hours corrected on Jan 15, 2025:
    ///   Hour 10 (day):  0.500 → 0.750, delta = +0.250 kWh, spot = 85 øre
    ///   Hour 17 (peak): 1.200 → 1.500, delta = +0.300 kWh, spot = 125 øre
    ///   Hour 22 (late): 0.400 → 0.200, delta = -0.200 kWh, spot = 55 øre
    ///   Net delta: +0.350 kWh
    ///
    /// Energy delta:        0.250×0.89 + 0.300×1.29 + (-0.200)×0.59 = 0.4915 → 0.49
    /// Grid tariff delta:   0.250×0.18 + 0.300×0.54 + (-0.200)×0.06 = 0.1950 → 0.20
    /// System delta:        0.350 × 0.054 = 0.0189 → 0.02
    /// Transmission delta:  0.350 × 0.049 = 0.01715 → 0.02
    /// Electricity tax:     0.350 × 0.008 = 0.0028 → 0.00
    /// Subtotal:            0.73
    /// VAT (25%):           0.1825 → 0.18
    /// Total:               0.91
    /// </summary>
    [Fact]
    public void Correction_delta_matches_golden_master()
    {
        var jan15 = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var deltas = new List<ConsumptionDelta>
        {
            new(jan15.AddHours(10), OldKwh: 0.500m, NewKwh: 0.750m),
            new(jan15.AddHours(17), OldKwh: 1.200m, NewKwh: 1.500m),
            new(jan15.AddHours(22), OldKwh: 0.400m, NewKwh: 0.200m),
        };

        var spotPrices = deltas.Select(d =>
            new SpotPriceRow(PriceArea, d.Timestamp, SpotPriceForHour(d.Timestamp.Hour))).ToList();

        var request = new CorrectionRequest(
            Gsrn,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            deltas, spotPrices, GridTariffRates(),
            SystemRate, TransmissionRate, ElectricityTaxRate,
            MarginPerKwh, 0m);

        var engine = new CorrectionEngine();
        var result = engine.Calculate(request);

        result.TotalDeltaKwh.Should().Be(0.350m);

        AssertCorrectionLine(result, "energy", 0.49m);
        AssertCorrectionLine(result, "grid_tariff", 0.20m);
        AssertCorrectionLine(result, "system_tariff", 0.02m);
        AssertCorrectionLine(result, "transmission_tariff", 0.02m);
        AssertCorrectionLine(result, "electricity_tax", 0.00m);

        result.Subtotal.Should().Be(0.73m);
        result.VatAmount.Should().Be(0.18m);
        result.Total.Should().Be(0.91m);
    }

    /// <summary>
    /// Golden Master #6: Erroneous switch reversal (2 months)
    ///
    /// Scenario: A supplier switch was made in error. Customer was with us for
    /// January + February 2025. Both months were settled. Now BRS-042 reverses it.
    ///
    /// January: GM#1 = 793.14 DKK (31 days, 409.200 kWh)
    /// February: 28 days, 672 hours
    ///   Consumption: 28 × 13.200 = 369.600 kWh
    ///   Energy: 28 × 12.468 = 349.104 → 349.10
    ///   Grid: 28 × 3.696 = 103.488 → 103.49
    ///   System: 369.600 × 0.054 = 19.9584 → 19.96
    ///   Transmission: 369.600 × 0.049 = 18.1104 → 18.11
    ///   Electricity tax: 369.600 × 0.008 = 2.9568 → 2.96
    ///   Grid sub: 49.00 × 28/28 = 49.00
    ///   Supplier sub: 39.00 × 28/28 = 39.00
    ///   Subtotal: 581.62
    ///   VAT: 581.62 × 0.25 = 145.405 → 145.41
    ///   Total: 581.62 + 145.41 = 727.03
    ///
    /// Total credited: 793.14 + 727.03 = 1520.17
    /// </summary>
    [Fact]
    public void Erroneous_switch_reversal_matches_golden_master()
    {
        var janRequest = BuildRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        var febRequest = BuildRequest(new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 1));

        var service = new ErroneousSwitchService(Engine);
        var result = service.CalculateReversal(new[] { janRequest, febRequest });

        result.CreditNotes.Should().HaveCount(2);
        result.CreditNotes[0].Total.Should().Be(793.14m);
        result.CreditNotes[1].Total.Should().Be(727.02m); // VAT 145.405 → banker's rounds to 145.40
        result.TotalCredited.Should().Be(1520.16m);
    }

    /// <summary>
    /// Golden Master #7: Elvarme customer crossing 4,000 kWh threshold mid-period
    ///
    /// Setup: Same consumption pattern as GM#1 (13.200 kWh/day).
    /// Customer had 3,800 kWh cumulative before January.
    /// January adds 409.200 kWh.
    /// Threshold crossed after: (4000 - 3800) / 13.200 ≈ 15.15 hours into the period.
    ///
    /// First 15 hours at standard rate (0.008):
    ///   Hours 0-5 (6 hours): 6 × 0.300 = 1.800 kWh
    ///   Hours 6-14 (9 hours): 9 × 0.500 = 4.500 kWh
    ///   Subtotal: 6.300 kWh, cumulative: 3806.300
    ///   But threshold is crossed at cumulative 4000.
    ///   Hour 0-5: 6 × 0.300 = 1.800, cumulative after: 3801.800
    ///   Hour 6: 0.500, cumulative: 3802.300
    ///   ...keep going until cumulative reaches 4000.
    ///
    /// Actually, let's calculate precisely:
    ///   After midnight start at 3800 kWh cumulative.
    ///   Need 200 kWh more to reach threshold.
    ///   Hours 0-5: 6 × 0.300 = 1.800, cumulative: 3801.800
    ///   Hours 6-14: 9 × 0.500 = 4.500, cumulative: 3806.300 (still below by a lot)
    ///
    /// With only 13.200 kWh/day, it takes 200/13.200 = 15.15 days to add 200 kWh.
    /// So threshold is crossed during day 16 (Jan 16).
    ///
    /// Days 1-15 (360 hours): 15 × 13.200 = 198.000 kWh, cumulative: 3998.000
    /// Day 16, Hour 0 (night): 0.300 kWh, cumulative: 3998.300 (below)
    /// Day 16, Hour 1: 0.300, cumulative: 3998.600
    /// Day 16, Hour 2: 0.300, cumulative: 3998.900
    /// Day 16, Hour 3: 0.300, cumulative: 3999.200
    /// Day 16, Hour 4: 0.300, cumulative: 3999.500
    /// Day 16, Hour 5: 0.300, cumulative: 3999.800
    /// Day 16, Hour 6 (day): 0.500, cumulative: 4000.300 — CROSSES HERE
    ///   Split: 0.200 at standard (0.008), 0.300 at reduced (0.005)
    /// Day 16, Hours 7-23: all at reduced rate
    /// Days 17-31: all at reduced rate
    ///
    /// Electricity tax calculation:
    ///   Standard rate hours (3800 → 4000 = 200.000 kWh): 200.000 × 0.008 = 1.600
    ///   Reduced rate hours (remaining 209.200 kWh): 209.200 × 0.005 = 1.046
    ///   Total electricity tax: 2.646 → 2.65
    ///
    /// All other lines remain same as GM#1:
    ///   Energy: 386.51, Grid: 114.58, System: 22.10, Transmission: 20.05
    ///   Grid sub: 49.00, Supplier sub: 39.00
    ///
    /// Subtotal: 386.51 + 114.58 + 22.10 + 20.05 + 2.65 + 49.00 + 39.00 = 633.89
    /// VAT: 633.89 × 0.25 = 158.4725 → 158.47
    /// Total: 633.89 + 158.47 = 792.36
    /// </summary>
    [Fact]
    public void Elvarme_threshold_crossing_matches_golden_master()
    {
        var request = BuildRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1))
            with { Elvarme = new ElvarmeOptions(ReducedElectricityTaxRate: 0.005m, CumulativeKwhBeforePeriod: 3800m) };
        var result = Engine.Calculate(request);

        result.TotalKwh.Should().Be(409.200m);

        // All non-tax lines are the same as GM#1
        AssertLine(result, "energy", 386.51m);
        AssertLine(result, "grid_tariff", 114.58m);
        AssertLine(result, "system_tariff", 22.10m);
        AssertLine(result, "transmission_tariff", 20.05m);

        // Electricity tax: split rate (200 kWh at 0.008, 209.200 kWh at 0.005)
        AssertLine(result, "electricity_tax", 2.65m);

        AssertLine(result, "grid_subscription", 49.00m);
        AssertLine(result, "supplier_subscription", 39.00m);

        result.Subtotal.Should().Be(633.89m);
        result.VatAmount.Should().Be(158.47m);
        result.Total.Should().Be(792.36m);
    }

    /// <summary>
    /// Golden Master #8: Solar customer with E18 production (1 day = Jan 1)
    ///
    /// Consumption (same pattern as GM#1 for 1 day = 13.200 kWh):
    ///   Hours 0-5: 0.300 kWh each (night)
    ///   Hours 6-15: 0.500 kWh each (day)
    ///   Hours 16-19: 1.200 kWh each (peak)
    ///   Hours 20-23: 0.400 kWh each (late)
    ///
    /// Production (solar, only during daylight hours 8-16):
    ///   Hours 8-9:   0.200 kWh each (morning ramp-up)
    ///   Hours 10-14: 0.600 kWh each (peak solar)
    ///   Hour 15:     0.300 kWh (afternoon decline)
    ///   Hour 16:     0.100 kWh (late afternoon)
    ///   Total production: 0.200×2 + 0.600×5 + 0.300 + 0.100 = 3.700 kWh
    ///
    /// Net per hour:
    ///   Hours 0-7:   no production → net = consumption (unchanged)
    ///   Hour 8:      0.500 - 0.200 = 0.300 net consumption
    ///   Hour 9:      0.500 - 0.200 = 0.300 net consumption
    ///   Hour 10:     0.500 - 0.600 = -0.100 excess production (CREDIT)
    ///   Hour 11:     0.500 - 0.600 = -0.100 excess production (CREDIT)
    ///   Hour 12:     0.500 - 0.600 = -0.100 excess production (CREDIT)
    ///   Hour 13:     0.500 - 0.600 = -0.100 excess production (CREDIT)
    ///   Hour 14:     0.500 - 0.600 = -0.100 excess production (CREDIT)
    ///   Hour 15:     0.500 - 0.300 = 0.200 net consumption
    ///   Hour 16:     1.200 - 0.100 = 1.100 net consumption
    ///   Hours 17-23: no production → net = consumption (unchanged)
    ///
    /// Net consumption hours (billable):
    ///   Hours 0-5:   6 × 0.300 = 1.800
    ///   Hours 6-7:   2 × 0.500 = 1.000
    ///   Hours 8-9:   2 × 0.300 = 0.600
    ///   Hour 15:     0.200
    ///   Hour 16:     1.100
    ///   Hours 17-19: 3 × 1.200 = 3.600
    ///   Hours 20-23: 4 × 0.400 = 1.600
    ///   Total net consumption: 9.900 kWh
    ///
    /// Production credit hours:
    ///   Hours 10-14: 5 × 0.100 = 0.500 kWh excess, all at spot 85 øre = 0.85 DKK/kWh
    ///   Credit: 0.500 × 0.85 = 0.425 → -0.43 (negative line)
    ///
    /// Energy (on net consumption):
    ///   Hours 0-5:   1.800 × (0.45+0.04) = 1.800 × 0.49 = 0.882
    ///   Hours 6-7:   1.000 × (0.85+0.04) = 1.000 × 0.89 = 0.890
    ///   Hours 8-9:   0.600 × (0.85+0.04) = 0.600 × 0.89 = 0.534
    ///   Hour 15:     0.200 × (0.85+0.04) = 0.200 × 0.89 = 0.178
    ///   Hour 16:     1.100 × (1.25+0.04) = 1.100 × 1.29 = 1.419
    ///   Hours 17-19: 3.600 × (1.25+0.04) = 3.600 × 1.29 = 4.644
    ///   Hours 20-23: 1.600 × (0.55+0.04) = 1.600 × 0.59 = 0.944
    ///   Total energy: 9.491 → 9.49
    ///
    /// Grid tariff (on net consumption):
    ///   Hours 0-5 (night, 0.06): 1.800 × 0.06 = 0.108
    ///   Hours 6-7 (day, 0.18):   1.000 × 0.18 = 0.180
    ///   Hours 8-9 (day, 0.18):   0.600 × 0.18 = 0.108
    ///   Hour 15 (day, 0.18):     0.200 × 0.18 = 0.036
    ///   Hour 16 (peak, 0.54):    1.100 × 0.54 = 0.594
    ///   Hours 17-19 (peak, 0.54): 3.600 × 0.54 = 1.944
    ///   Hours 20-23 (night, 0.06): 1.600 × 0.06 = 0.096
    ///   Total grid tariff: 3.066 → 3.07
    ///
    /// System (on net): 9.900 × 0.054 = 0.5346 → 0.53
    /// Transmission:    9.900 × 0.049 = 0.4851 → 0.49
    /// Electricity tax: 9.900 × 0.008 = 0.0792 → 0.08
    /// Grid sub (1/31): 49.00 × 1/31 = 1.580... → 1.58
    /// Supplier sub:    39.00 × 1/31 = 1.258... → 1.26
    ///
    /// Subtotal: 9.49 + 3.07 + 0.53 + 0.49 + 0.08 + 1.58 + 1.26 + (-0.43) = 16.07
    /// VAT: 16.07 × 0.25 = 4.0175 → 4.02
    /// Total: 16.07 + 4.02 = 20.09
    /// </summary>
    [Fact]
    public void Solar_net_settlement_matches_golden_master()
    {
        var day = new DateOnly(2025, 1, 1);
        var nextDay = new DateOnly(2025, 1, 2);

        var consumption = new List<MeteringDataRow>();
        var spotPrices = new List<SpotPriceRow>();
        var production = new List<MeteringDataRow>();

        for (var h = 0; h < 24; h++)
        {
            var ts = new DateTime(2025, 1, 1, h, 0, 0, DateTimeKind.Utc);
            consumption.Add(new MeteringDataRow(ts, "PT1H", ConsumptionForHour(h), "A03", "gm8"));
            spotPrices.Add(new SpotPriceRow(PriceArea, ts, SpotPriceForHour(h)));

            // Solar production during hours 8-16
            var prodKwh = h switch
            {
                8 or 9 => 0.200m,
                >= 10 and <= 14 => 0.600m,
                15 => 0.300m,
                16 => 0.100m,
                _ => 0m,
            };
            if (prodKwh > 0)
                production.Add(new MeteringDataRow(ts, "PT1H", prodKwh, "A03", "gm8-prod"));
        }

        var request = new SettlementRequest(
            Gsrn, day, nextDay,
            consumption, spotPrices, GridTariffRates(),
            SystemRate, TransmissionRate, ElectricityTaxRate,
            GridSubscription, MarginPerKwh, 0m, SupplierSubscription,
            Production: production);

        var result = Engine.Calculate(request);

        result.TotalKwh.Should().Be(13.200m); // gross consumption unchanged

        AssertLine(result, "energy", 9.49m);
        AssertLine(result, "grid_tariff", 3.07m);
        AssertLine(result, "system_tariff", 0.53m);
        AssertLine(result, "transmission_tariff", 0.49m);
        AssertLine(result, "electricity_tax", 0.08m);
        AssertLine(result, "grid_subscription", 1.58m);
        AssertLine(result, "supplier_subscription", 1.26m);

        var creditLine = result.Lines.Single(l => l.ChargeType == "production_credit");
        creditLine.Amount.Should().Be(-0.42m); // 0.500 × 0.85 = 0.425 → banker's rounds to 0.42

        result.Subtotal.Should().Be(16.08m);
        result.VatAmount.Should().Be(4.02m);
        result.Total.Should().Be(20.10m);
    }

    /// <summary>
    /// Golden Master #9: Correction during supply period overlap
    ///
    /// Scenario: Our supply starts Jan 16. A correction arrives for Jan 1-31
    /// covering the full month. We should only settle the delta for hours
    /// during OUR supply period (Jan 16-31 = 16 days = 384 hours).
    ///
    /// Correction: hours 10 and 17 on Jan 20 changed
    ///   Hour 10: 0.500 → 0.700, delta = +0.200, spot = 85 øre
    ///   Hour 17: 1.200 → 0.900, delta = -0.300, spot = 125 øre
    ///   Net delta: -0.100 kWh
    ///
    /// Only these 2 hours are within our supply period, so only these count.
    ///
    /// Energy delta:
    ///   Hour 10: 0.200 × (0.85+0.04) = 0.200 × 0.89 = 0.178
    ///   Hour 17: -0.300 × (1.25+0.04) = -0.300 × 1.29 = -0.387
    ///   Total: -0.209 → -0.21
    ///
    /// Grid tariff delta:
    ///   Hour 10 (day, 0.18): 0.200 × 0.18 = 0.036
    ///   Hour 17 (peak, 0.54): -0.300 × 0.54 = -0.162
    ///   Total: -0.126 → -0.13
    ///
    /// System: -0.100 × 0.054 = -0.0054 → -0.01
    /// Transmission: -0.100 × 0.049 = -0.0049 → 0.00
    /// Electricity tax: -0.100 × 0.008 = -0.0008 → 0.00
    /// Subtotal: -0.21 + (-0.13) + (-0.01) + 0.00 + 0.00 = -0.35
    /// VAT: -0.35 × 0.25 = -0.0875 → -0.09
    /// Total: -0.35 + (-0.09) = -0.44
    /// </summary>
    [Fact]
    public void Correction_filtered_to_supply_period_matches_golden_master()
    {
        // Our supply period: Jan 16 - Feb 1
        var supplyStart = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        var supplyEnd = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        // Full correction arrives for Jan 1-31, but we filter to our supply period
        var jan20 = new DateTime(2025, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        var allDeltas = new List<ConsumptionDelta>
        {
            // These are within our supply period
            new(jan20.AddHours(10), OldKwh: 0.500m, NewKwh: 0.700m),
            new(jan20.AddHours(17), OldKwh: 1.200m, NewKwh: 0.900m),
        };

        // Filter: only deltas within our supply period
        var filteredDeltas = allDeltas
            .Where(d => d.Timestamp >= supplyStart && d.Timestamp < supplyEnd)
            .ToList();

        var spotPrices = filteredDeltas.Select(d =>
            new SpotPriceRow(PriceArea, d.Timestamp, SpotPriceForHour(d.Timestamp.Hour))).ToList();

        var request = new CorrectionRequest(
            Gsrn,
            new DateOnly(2025, 1, 16), new DateOnly(2025, 2, 1),
            filteredDeltas, spotPrices, GridTariffRates(),
            SystemRate, TransmissionRate, ElectricityTaxRate,
            MarginPerKwh, 0m);

        var engine = new CorrectionEngine();
        var result = engine.Calculate(request);

        result.TotalDeltaKwh.Should().Be(-0.100m);

        AssertCorrectionLine(result, "energy", -0.21m);
        AssertCorrectionLine(result, "grid_tariff", -0.13m);
        AssertCorrectionLine(result, "system_tariff", -0.01m);
        AssertCorrectionLine(result, "transmission_tariff", 0.00m);
        AssertCorrectionLine(result, "electricity_tax", 0.00m);

        result.Subtotal.Should().Be(-0.35m);
        result.VatAmount.Should().Be(-0.09m);
        result.Total.Should().Be(-0.44m);
    }

    /// <summary>
    /// Golden Master #10: Tariff change mid-billing period
    ///
    /// Jan 1-15 (15 days): old grid tariffs
    /// Jan 16-31 (16 days): new grid tariffs (50% higher)
    ///
    /// Old grid rates: night 0.06, day 0.18, peak 0.54 (same as GM#1)
    /// New grid rates: night 0.09, day 0.27, peak 0.81 (50% higher)
    ///
    /// Before (Jan 1-15):
    ///   kWh: 15 × 13.200 = 198.000
    ///   Energy: 15 × 12.468 = 187.020
    ///   Grid: 15 × 3.696 = 55.440
    ///   System: 198.000 × 0.054 = 10.692
    ///   Transmission: 198.000 × 0.049 = 9.702
    ///   Electricity tax: 198.000 × 0.008 = 1.584
    ///   Grid sub: 49.00 × 15/31 = 23.709...
    ///   Supplier sub: 39.00 × 15/31 = 18.870...
    ///
    /// After (Jan 16-31):
    ///   kWh: 16 × 13.200 = 211.200
    ///   Energy: 16 × 12.468 = 199.488
    ///   Grid (new rates): 16 × (6×0.300×0.09 + 10×0.500×0.27 + 4×1.200×0.81 + 4×0.400×0.09)
    ///     = 16 × (0.162 + 1.350 + 3.888 + 0.144) = 16 × 5.544 = 88.704
    ///   System: 211.200 × 0.054 = 11.4048
    ///   Transmission: 211.200 × 0.049 = 10.3488
    ///   Electricity tax: 211.200 × 0.008 = 1.6896
    ///   Grid sub: 49.00 × 16/31 = 25.290...
    ///   Supplier sub: 39.00 × 16/31 = 20.129...
    ///
    /// Combined (rounded per sub-period, then summed):
    ///   Energy: 187.02 + 199.49 = 386.51
    ///   Grid: 55.44 + 88.70 = 144.14
    ///   System: 10.69 + 11.40 = 22.09
    ///   Transmission: 9.70 + 10.35 = 20.05
    ///   Electricity tax: 1.58 + 1.69 = 3.27
    ///   Grid sub: 23.71 + 25.29 = 49.00
    ///   Supplier sub: 18.87 + 20.13 = 39.00
    ///   Subtotal: 664.06
    ///   VAT: 664.06 × 0.25 = 166.015 → 166.02
    ///   Total: 664.06 + 166.02 = 830.08
    /// </summary>
    [Fact]
    public void Tariff_change_mid_period_matches_golden_master()
    {
        var request = BuildRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));

        // New grid tariff rates (50% higher)
        var newGridRates = new List<TariffRateRow>();
        for (var h = 1; h <= 24; h++)
        {
            var rate = h switch
            {
                >= 1 and <= 6 => 0.09m,
                >= 7 and <= 16 => 0.27m,
                >= 17 and <= 20 => 0.81m,
                _ => 0.09m,
            };
            newGridRates.Add(new TariffRateRow(h, rate));
        }

        var splitter = new PeriodSplitter(Engine);
        var result = splitter.CalculateWithTariffChange(
            request,
            tariffChangeDate: new DateOnly(2025, 1, 16),
            newGridTariffRates: newGridRates);

        result.TotalKwh.Should().Be(409.200m);

        AssertLine(result, "energy", 386.51m);
        AssertLine(result, "grid_tariff", 144.14m);
        AssertLine(result, "system_tariff", 22.09m);
        AssertLine(result, "transmission_tariff", 20.05m);
        AssertLine(result, "electricity_tax", 3.27m);
        AssertLine(result, "grid_subscription", 49.00m);
        AssertLine(result, "supplier_subscription", 39.00m);

        result.Subtotal.Should().Be(664.06m);
        result.VatAmount.Should().Be(166.02m);
        result.Total.Should().Be(830.08m);
    }

    private static void AssertCorrectionLine(CorrectionResult result, string chargeType, decimal expectedAmount)
    {
        var line = result.Lines.Single(l => l.ChargeType == chargeType);
        line.Amount.Should().Be(expectedAmount, $"correction {chargeType} should be {expectedAmount}");
    }

    private static void AssertLine(SettlementResult result, string chargeType, decimal expectedAmount)
    {
        var line = result.Lines.Single(l => l.ChargeType == chargeType);
        line.Amount.Should().Be(expectedAmount, $"{chargeType} should be {expectedAmount}");
    }
}
