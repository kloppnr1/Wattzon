using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class PeriodSplitterTests
{
    private readonly SettlementEngine _engine = new();
    private readonly PeriodSplitter _sut;

    public PeriodSplitterTests()
    {
        _sut = new PeriodSplitter(_engine);
    }

    private static SettlementRequest MakeRequest(
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<MeteringDataRow> consumption,
        IReadOnlyList<SpotPriceRow> spotPrices,
        IReadOnlyList<TariffRateRow> gridRates)
    {
        return new SettlementRequest(
            "571313100000099999",
            periodStart,
            periodEnd,
            consumption,
            spotPrices,
            gridRates,
            SystemTariffRate: 0.054m,
            TransmissionTariffRate: 0.049m,
            ElectricityTaxRate: 0.008m,
            GridSubscriptionPerMonth: 49.00m,
            MarginPerKwh: 0.04m,
            SupplementPerKwh: 0m,
            SupplierSubscriptionPerMonth: 39.00m);
    }

    private static List<MeteringDataRow> MakeHourlyConsumption(DateTime start, int hours, decimal kwhPerHour)
    {
        return Enumerable.Range(0, hours)
            .Select(h => new MeteringDataRow(start.AddHours(h), "PT1H", kwhPerHour, "A01", "msg-001"))
            .ToList();
    }

    private static List<SpotPriceRow> MakeHourlySpotPrices(DateTime start, int hours, decimal priceOrePerKwh)
    {
        return Enumerable.Range(0, hours)
            .Select(h => new SpotPriceRow("DK1", start.AddHours(h), priceOrePerKwh))
            .ToList();
    }

    private static List<TariffRateRow> MakeGridRates(decimal rate)
    {
        return Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, rate)).ToList();
    }

    [Fact]
    public void Split_on_first_day_puts_all_consumption_in_after_period()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var consumption = MakeHourlyConsumption(start, 48, 0.5m); // 2 days
        var spots = MakeHourlySpotPrices(start, 48, 80m);
        var gridRates = MakeGridRates(0.20m);
        var newGridRates = MakeGridRates(0.30m);

        var request = MakeRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3), consumption, spots, gridRates);

        var result = _sut.CalculateWithTariffChange(request, new DateOnly(2025, 1, 1), newGridRates);

        result.TotalKwh.Should().Be(24m); // 48 * 0.5
        result.PeriodStart.Should().Be(new DateOnly(2025, 1, 1));
        result.PeriodEnd.Should().Be(new DateOnly(2025, 1, 3));
    }

    [Fact]
    public void Split_on_last_day_puts_all_consumption_in_before_period()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var consumption = MakeHourlyConsumption(start, 48, 0.5m);
        var spots = MakeHourlySpotPrices(start, 48, 80m);
        var gridRates = MakeGridRates(0.20m);
        var newGridRates = MakeGridRates(0.30m);

        var request = MakeRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3), consumption, spots, gridRates);

        var result = _sut.CalculateWithTariffChange(request, new DateOnly(2025, 1, 3), newGridRates);

        result.TotalKwh.Should().Be(24m);
        result.PeriodStart.Should().Be(new DateOnly(2025, 1, 1));
        result.PeriodEnd.Should().Be(new DateOnly(2025, 1, 3));
    }

    [Fact]
    public void Split_produces_correct_combined_kwh()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var consumption = MakeHourlyConsumption(start, 48, 0.5m); // 2 days, 24 kWh total
        var spots = MakeHourlySpotPrices(start, 48, 80m);
        var gridRates = MakeGridRates(0.20m);
        var newGridRates = MakeGridRates(0.30m);

        var request = MakeRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3), consumption, spots, gridRates);

        var result = _sut.CalculateWithTariffChange(request, new DateOnly(2025, 1, 2), newGridRates);

        result.TotalKwh.Should().Be(24m); // 12 + 12
    }

    [Fact]
    public void Higher_tariff_in_second_period_increases_total()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var consumption = MakeHourlyConsumption(start, 48, 0.5m);
        var spots = MakeHourlySpotPrices(start, 48, 80m);
        var lowGridRates = MakeGridRates(0.10m);
        var highGridRates = MakeGridRates(0.50m);

        var requestLow = MakeRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3), consumption, spots, lowGridRates);

        // Split at day 2: first day low rates, second day high rates
        var splitResult = _sut.CalculateWithTariffChange(requestLow, new DateOnly(2025, 1, 2), highGridRates);

        // Compare to all-low-rate settlement
        var allLowResult = _engine.Calculate(requestLow);

        splitResult.Total.Should().BeGreaterThan(allLowResult.Total);
    }

    [Fact]
    public void Result_has_all_standard_charge_types()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var consumption = MakeHourlyConsumption(start, 24, 0.5m);
        var spots = MakeHourlySpotPrices(start, 24, 80m);
        var gridRates = MakeGridRates(0.20m);
        var newGridRates = MakeGridRates(0.30m);

        var request = MakeRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2), consumption, spots, gridRates);

        var result = _sut.CalculateWithTariffChange(request, new DateOnly(2025, 1, 1), newGridRates);

        var types = result.Lines.Select(l => l.ChargeType).ToList();
        types.Should().Contain("energy");
        types.Should().Contain("grid_tariff");
        types.Should().Contain("system_tariff");
        types.Should().Contain("transmission_tariff");
        types.Should().Contain("electricity_tax");
    }

    [Fact]
    public void Vat_is_recalculated_on_combined_subtotal()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var consumption = MakeHourlyConsumption(start, 48, 0.5m);
        var spots = MakeHourlySpotPrices(start, 48, 80m);
        var gridRates = MakeGridRates(0.20m);
        var newGridRates = MakeGridRates(0.30m);

        var request = MakeRequest(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3), consumption, spots, gridRates);

        var result = _sut.CalculateWithTariffChange(request, new DateOnly(2025, 1, 2), newGridRates);

        result.VatAmount.Should().Be(Math.Round(result.Subtotal * 0.25m, 2));
        result.Total.Should().Be(result.Subtotal + result.VatAmount);
    }
}
