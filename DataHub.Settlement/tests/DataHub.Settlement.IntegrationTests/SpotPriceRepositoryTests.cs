using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Infrastructure.Metering;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class SpotPriceRepositoryTests
{
    private readonly SpotPriceRepository _sut = new(TestDatabase.ConnectionString);
    private readonly TestDatabase _db;

    public SpotPriceRepositoryTests(TestDatabase db)
    {
        _db = db;
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Store_and_get_single_price()
    {
        var prices = new List<SpotPriceRow>
        {
            new("DK1", Utc(2025, 1, 1, 0), 0.00045m),
        };

        await _sut.StorePricesAsync(prices, CancellationToken.None);

        var result = await _sut.GetPriceAsync("DK1", Utc(2025, 1, 1, 0), CancellationToken.None);

        result.Should().Be(0.00045m);
    }

    [Fact]
    public async Task Store_and_get_range_returns_correct_subset()
    {
        var prices = new List<SpotPriceRow>
        {
            new("DK1", Utc(2025, 1, 1, 0), 0.00045m),
            new("DK1", Utc(2025, 1, 1, 1), 0.00042m),
            new("DK1", Utc(2025, 1, 1, 2), 0.00038m),
            new("DK1", Utc(2025, 1, 1, 3), 0.00040m),
        };

        await _sut.StorePricesAsync(prices, CancellationToken.None);

        var result = await _sut.GetPricesAsync(
            "DK1",
            Utc(2025, 1, 1, 1),
            Utc(2025, 1, 1, 3),
            CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].PricePerKwh.Should().Be(0.00042m);
        result[1].PricePerKwh.Should().Be(0.00038m);
    }

    [Fact]
    public async Task Upsert_replaces_existing_price()
    {
        var original = new List<SpotPriceRow>
        {
            new("DK2", Utc(2025, 1, 1, 12), 0.00085m),
        };

        await _sut.StorePricesAsync(original, CancellationToken.None);

        var updated = new List<SpotPriceRow>
        {
            new("DK2", Utc(2025, 1, 1, 12), 0.00090m),
        };

        await _sut.StorePricesAsync(updated, CancellationToken.None);

        var result = await _sut.GetPriceAsync("DK2", Utc(2025, 1, 1, 12), CancellationToken.None);

        result.Should().Be(0.00090m);
    }
}
