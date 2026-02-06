using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.Infrastructure.Tariff;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class TariffRepositoryTests
{
    private readonly TariffRepository _sut;
    private readonly PortfolioRepository _portfolio;

    public TariffRepositoryTests(TestDatabase db)
    {
        _sut = new TariffRepository(TestDatabase.ConnectionString);
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
    }

    private async Task EnsureGridAreaAsync()
    {
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", CancellationToken.None);
    }

    [Fact]
    public async Task GetRatesAsync_returns_24_hourly_grid_tariff_rates()
    {
        await EnsureGridAreaAsync();

        var rates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
        {
            >= 1 and <= 6 => 0.06m,
            >= 7 and <= 16 => 0.18m,
            >= 17 and <= 20 => 0.54m,
            _ => 0.06m,
        })).ToList();

        await _sut.SeedGridTariffAsync("344", "grid", new DateOnly(2025, 1, 1), rates, CancellationToken.None);

        var result = await _sut.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), CancellationToken.None);

        result.Should().HaveCount(24);
        result.First(r => r.HourNumber == 1).PricePerKwh.Should().Be(0.06m);
        result.First(r => r.HourNumber == 10).PricePerKwh.Should().Be(0.18m);
        result.First(r => r.HourNumber == 18).PricePerKwh.Should().Be(0.54m);
    }

    [Fact]
    public async Task GetRatesAsync_returns_flat_rate_for_system_tariff()
    {
        await EnsureGridAreaAsync();

        var rates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, 0.054m)).ToList();
        await _sut.SeedGridTariffAsync("344", "system", new DateOnly(2025, 1, 1), rates, CancellationToken.None);

        var result = await _sut.GetRatesAsync("344", "system", new DateOnly(2025, 1, 15), CancellationToken.None);

        result.Should().HaveCount(24);
        result.Should().AllSatisfy(r => r.PricePerKwh.Should().Be(0.054m));
    }

    [Fact]
    public async Task GetSubscriptionAsync_returns_monthly_amount()
    {
        await EnsureGridAreaAsync();

        await _sut.SeedSubscriptionAsync("344", "grid", 49.00m, new DateOnly(2025, 1, 1), CancellationToken.None);

        var amount = await _sut.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), CancellationToken.None);

        amount.Should().Be(49.00m);
    }

    [Fact]
    public async Task GetElectricityTaxAsync_returns_rate()
    {
        await _sut.SeedElectricityTaxAsync(0.008m, new DateOnly(2025, 1, 1), CancellationToken.None);

        var rate = await _sut.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), CancellationToken.None);

        rate.Should().Be(0.008m);
    }
}
