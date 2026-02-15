using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class SettlementDataLoaderTests
{
    [Fact]
    public async Task LoadAsync_assembles_correct_input()
    {
        var meteringRepo = new InMemoryMeteringDataRepository(
        [
            new MeteringDataRow(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), "PT1H", 0.5m, "A01", "msg-1"),
            new MeteringDataRow(new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc), "PT1H", 0.5m, "A01", "msg-1"),
        ]);
        var spotPriceRepo = new InMemorySpotPriceRepository(
        [
            new SpotPriceRow("DK1", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 85m),
            new SpotPriceRow("DK1", new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc), 90m),
        ]);
        var tariffRepo = new InMemoryTariffRepository(
            gridRates: [new TariffRateRow(1, 0.06m)],
            systemRates: [new TariffRateRow(1, 0.054m)],
            transmissionRates: [new TariffRateRow(1, 0.049m)],
            electricityTax: 0.008m,
            gridSubscription: 49.00m);

        var sut = new SettlementDataLoader(meteringRepo, spotPriceRepo, tariffRepo);

        var result = await sut.LoadAsync(
            "571313100000012345", "344", "DK1",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            0.04m, 0m, 39.00m,
            CancellationToken.None);

        result.MeteringPointId.Should().Be("571313100000012345");
        result.Consumption.Should().HaveCount(2);
        result.SpotPrices.Should().HaveCount(2);
        result.GridTariffRates.Should().HaveCount(1);
        result.ElectricityTaxRate.Should().Be(0.008m);
        result.GridSubscriptionPerMonth.Should().Be(49.00m);
        result.MarginPerKwh.Should().Be(0.04m);
        result.SupplierSubscriptionPerMonth.Should().Be(39.00m);
    }

    [Fact]
    public async Task LoadAsync_handles_empty_data()
    {
        var meteringRepo = new InMemoryMeteringDataRepository([]);
        var spotPriceRepo = new InMemorySpotPriceRepository([]);
        var tariffRepo = new InMemoryTariffRepository(
            gridRates: [],
            systemRates: [new TariffRateRow(1, 0.054m)],
            transmissionRates: [new TariffRateRow(1, 0.049m)],
            electricityTax: 0.008m,
            gridSubscription: 0m);

        var sut = new SettlementDataLoader(meteringRepo, spotPriceRepo, tariffRepo);

        var result = await sut.LoadAsync(
            "571313100000012345", "344", "DK1",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            0.04m, 0m, 39.00m,
            CancellationToken.None);

        result.Consumption.Should().BeEmpty();
        result.SpotPrices.Should().BeEmpty();
        result.GridTariffRates.Should().BeEmpty();
    }

    // ── In-memory test doubles ──

    private sealed class InMemoryMeteringDataRepository(IReadOnlyList<MeteringDataRow> rows) : IMeteringDataRepository
    {
        public Task StoreTimeSeriesAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct) => Task.CompletedTask;
        public Task<int> StoreTimeSeriesWithHistoryAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct) => Task.FromResult(0);
        public Task<IReadOnlyList<MeteringDataRow>> GetConsumptionAsync(string meteringPointId, DateTime from, DateTime to, CancellationToken ct) => Task.FromResult(rows);
        public Task<IReadOnlyList<MeteringDataChange>> GetChangesAsync(string meteringPointId, DateTime from, DateTime to, CancellationToken ct) => Task.FromResult<IReadOnlyList<MeteringDataChange>>([]);
    }

    private sealed class InMemorySpotPriceRepository(IReadOnlyList<SpotPriceRow> prices) : ISpotPriceRepository
    {
        public Task StorePricesAsync(IReadOnlyList<SpotPriceRow> prices, CancellationToken ct) => Task.CompletedTask;
        public Task<decimal> GetPriceAsync(string priceArea, DateTime hour, CancellationToken ct) => Task.FromResult(0m);
        public Task<IReadOnlyList<SpotPriceRow>> GetPricesAsync(string priceArea, DateTime from, DateTime to, CancellationToken ct) => Task.FromResult(prices);
        public Task<SpotPricePagedResult> GetPricesPagedAsync(string priceArea, DateTime from, DateTime to, int page, int pageSize, CancellationToken ct) => Task.FromResult(new SpotPricePagedResult(prices, prices.Count, 0m, 0m, 0m));
        public Task<DateOnly?> GetLatestPriceDateAsync(string priceArea, CancellationToken ct) => Task.FromResult<DateOnly?>(null);
        public Task<DateOnly?> GetEarliestPriceDateAsync(string priceArea, CancellationToken ct) => Task.FromResult<DateOnly?>(null);
        public Task<SpotPriceStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(new SpotPriceStatus(null, null, false, "warning"));
    }

    private sealed class InMemoryTariffRepository(
        IReadOnlyList<TariffRateRow> gridRates,
        IReadOnlyList<TariffRateRow> systemRates,
        IReadOnlyList<TariffRateRow> transmissionRates,
        decimal electricityTax,
        decimal gridSubscription) : ITariffRepository
    {
        public Task<IReadOnlyList<TariffRateRow>> GetRatesAsync(string gridAreaCode, string tariffType, DateOnly date, CancellationToken ct) =>
            Task.FromResult(tariffType switch
            {
                "system" => systemRates,
                "transmission" => transmissionRates,
                _ => gridRates,
            });
        public Task<decimal?> GetSubscriptionAsync(string gridAreaCode, string subscriptionType, DateOnly date, CancellationToken ct) => Task.FromResult<decimal?>(gridSubscription);
        public Task<decimal?> GetElectricityTaxAsync(DateOnly date, CancellationToken ct) => Task.FromResult<decimal?>(electricityTax);
        public Task SeedGridTariffAsync(string gridAreaCode, string tariffType, DateOnly validFrom, IReadOnlyList<TariffRateRow> rates, CancellationToken ct) => Task.CompletedTask;
        public Task SeedSubscriptionAsync(string gridAreaCode, string subscriptionType, decimal amountPerMonth, DateOnly validFrom, CancellationToken ct) => Task.CompletedTask;
        public Task SeedElectricityTaxAsync(decimal ratePerKwh, DateOnly validFrom, CancellationToken ct) => Task.CompletedTask;
        public Task StoreTariffAttachmentsAsync(string gsrn, IReadOnlyList<Application.Parsing.TariffAttachment> tariffs, string? correlationId, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<MeteringPointTariffAttachment>> GetAttachmentsForGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult<IReadOnlyList<MeteringPointTariffAttachment>>(Array.Empty<MeteringPointTariffAttachment>());
    }
}
