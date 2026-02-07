using DataHub.Settlement.Infrastructure.Metering;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class AnnualConsumptionTrackerTests
{
    private readonly AnnualConsumptionTracker _sut = new(TestDatabase.ConnectionString);

    public AnnualConsumptionTrackerTests(TestDatabase db) { }

    [Fact]
    public async Task Get_nonexistent_returns_zero()
    {
        var result = await _sut.GetCumulativeKwhAsync("571313100000011111", 2025, CancellationToken.None);

        result.Should().Be(0m);
    }

    [Fact]
    public async Task Update_creates_new_row_and_get_returns_value()
    {
        await _sut.UpdateCumulativeKwhAsync("571313100000099999", 2025, 150.5m, CancellationToken.None);

        var result = await _sut.GetCumulativeKwhAsync("571313100000099999", 2025, CancellationToken.None);
        result.Should().Be(150.5m);
    }

    [Fact]
    public async Task Update_accumulates_on_existing_row()
    {
        await _sut.UpdateCumulativeKwhAsync("571313100000088888", 2025, 100m, CancellationToken.None);
        await _sut.UpdateCumulativeKwhAsync("571313100000088888", 2025, 250m, CancellationToken.None);

        var result = await _sut.GetCumulativeKwhAsync("571313100000088888", 2025, CancellationToken.None);
        result.Should().Be(350m);
    }

    [Fact]
    public async Task Different_years_are_tracked_independently()
    {
        await _sut.UpdateCumulativeKwhAsync("571313100000077777", 2024, 3000m, CancellationToken.None);
        await _sut.UpdateCumulativeKwhAsync("571313100000077777", 2025, 500m, CancellationToken.None);

        var r2024 = await _sut.GetCumulativeKwhAsync("571313100000077777", 2024, CancellationToken.None);
        var r2025 = await _sut.GetCumulativeKwhAsync("571313100000077777", 2025, CancellationToken.None);

        r2024.Should().Be(3000m);
        r2025.Should().Be(500m);
    }

    [Fact]
    public async Task Different_metering_points_are_tracked_independently()
    {
        await _sut.UpdateCumulativeKwhAsync("571313100000066666", 2025, 200m, CancellationToken.None);
        await _sut.UpdateCumulativeKwhAsync("571313100000055555", 2025, 800m, CancellationToken.None);

        var r1 = await _sut.GetCumulativeKwhAsync("571313100000066666", 2025, CancellationToken.None);
        var r2 = await _sut.GetCumulativeKwhAsync("571313100000055555", 2025, CancellationToken.None);

        r1.Should().Be(200m);
        r2.Should().Be(800m);
    }
}
