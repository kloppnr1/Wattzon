using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class CorrectionEngineTests
{
    private readonly CorrectionEngine _sut = new();

    private static CorrectionRequest MakeRequest(
        IReadOnlyList<ConsumptionDelta> deltas,
        IReadOnlyList<SpotPriceRow>? spotPrices = null,
        IReadOnlyList<TariffRateRow>? gridRates = null)
    {
        var hour = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        spotPrices ??= new[] { new SpotPriceRow("DK1", hour, 80.0m) }; // 80 øre = 0.80 kr
        gridRates ??= new[] { new TariffRateRow(1, 0.20m) };

        return new CorrectionRequest(
            "571313100000099999",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 2),
            deltas,
            spotPrices,
            gridRates,
            SystemTariffRate: 0.054m,
            TransmissionTariffRate: 0.049m,
            ElectricityTaxRate: 0.008m,
            MarginPerKwh: 0.04m,
            SupplementPerKwh: 0m);
    }

    [Fact]
    public void Empty_deltas_produce_zero_result()
    {
        var request = MakeRequest(Array.Empty<ConsumptionDelta>());

        var result = _sut.Calculate(request);

        result.TotalDeltaKwh.Should().Be(0m);
        result.Subtotal.Should().Be(0m);
        result.VatAmount.Should().Be(0m);
        result.Total.Should().Be(0m);
    }

    [Fact]
    public void Single_positive_delta_produces_charge()
    {
        var hour = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var deltas = new[] { new ConsumptionDelta(hour, 0.300m, 0.500m) }; // +0.200 kWh

        var request = MakeRequest(deltas);
        var result = _sut.Calculate(request);

        result.TotalDeltaKwh.Should().Be(0.200m);
        result.Total.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Negative_delta_produces_credit()
    {
        var hour = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var deltas = new[] { new ConsumptionDelta(hour, 0.500m, 0.300m) }; // -0.200 kWh

        var request = MakeRequest(deltas);
        var result = _sut.Calculate(request);

        result.TotalDeltaKwh.Should().Be(-0.200m);
        result.Total.Should().BeLessThan(0m);
    }

    [Fact]
    public void Zero_delta_produces_zero_amounts()
    {
        var hour = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var deltas = new[] { new ConsumptionDelta(hour, 0.500m, 0.500m) }; // no change

        var request = MakeRequest(deltas);
        var result = _sut.Calculate(request);

        result.TotalDeltaKwh.Should().Be(0m);
        result.Lines.Should().AllSatisfy(l => l.Amount.Should().Be(0m));
    }

    [Fact]
    public void Multiple_deltas_are_summed()
    {
        var hour1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hour2 = new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        var deltas = new[]
        {
            new ConsumptionDelta(hour1, 0.300m, 0.500m), // +0.200
            new ConsumptionDelta(hour2, 0.400m, 0.350m), // -0.050
        };

        var spotPrices = new[]
        {
            new SpotPriceRow("DK1", hour1, 80.0m),
            new SpotPriceRow("DK1", hour2, 90.0m),
        };
        var gridRates = new[]
        {
            new TariffRateRow(1, 0.20m),
            new TariffRateRow(2, 0.20m),
        };

        var request = MakeRequest(deltas, spotPrices, gridRates);
        var result = _sut.Calculate(request);

        result.TotalDeltaKwh.Should().Be(0.150m);
        result.Lines.Should().HaveCount(5);
    }

    [Fact]
    public void Result_always_has_five_charge_types()
    {
        var hour = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var deltas = new[] { new ConsumptionDelta(hour, 0.300m, 0.500m) };

        var result = _sut.Calculate(MakeRequest(deltas));

        var types = result.Lines.Select(l => l.ChargeType).ToList();
        types.Should().Contain("energy");
        types.Should().Contain("grid_tariff");
        types.Should().Contain("system_tariff");
        types.Should().Contain("transmission_tariff");
        types.Should().Contain("electricity_tax");
    }

    [Fact]
    public void Vat_is_25_percent_of_subtotal()
    {
        var hour = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var deltas = new[] { new ConsumptionDelta(hour, 0.300m, 0.500m) };

        var result = _sut.Calculate(MakeRequest(deltas));

        result.VatAmount.Should().Be(Math.Round(result.Subtotal * 0.25m, 2));
        result.Total.Should().Be(result.Subtotal + result.VatAmount);
    }
}
