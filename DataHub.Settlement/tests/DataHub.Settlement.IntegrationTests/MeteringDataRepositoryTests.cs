using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Infrastructure.Metering;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class MeteringDataRepositoryTests
{
    private readonly MeteringDataRepository _sut = new(TestDatabase.ConnectionString);
    private readonly TestDatabase _db;

    public MeteringDataRepositoryTests(TestDatabase db)
    {
        _db = db;
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Store_and_retrieve_returns_matching_rows()
    {
        var rows = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.300m, "A03", "msg-001"),
            new(Utc(2025, 1, 1, 1), "PT1H", 0.300m, "A03", "msg-001"),
            new(Utc(2025, 1, 1, 2), "PT1H", 0.500m, "A03", "msg-001"),
        };

        await _sut.StoreTimeSeriesAsync("571313100000012345", rows, CancellationToken.None);

        var result = await _sut.GetConsumptionAsync(
            "571313100000012345",
            Utc(2025, 1, 1),
            Utc(2025, 1, 2),
            CancellationToken.None);

        result.Should().HaveCount(3);
        result[0].QuantityKwh.Should().Be(0.300m);
        result[2].QuantityKwh.Should().Be(0.500m);
    }

    [Fact]
    public async Task Upsert_replaces_existing_data()
    {
        var original = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.300m, "A03", "msg-001"),
        };

        await _sut.StoreTimeSeriesAsync("571313100000012345", original, CancellationToken.None);

        var correction = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.450m, "A01", "msg-002"),
        };

        await _sut.StoreTimeSeriesAsync("571313100000012345", correction, CancellationToken.None);

        var result = await _sut.GetConsumptionAsync(
            "571313100000012345",
            Utc(2025, 1, 1),
            Utc(2025, 1, 2),
            CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].QuantityKwh.Should().Be(0.450m);
        result[0].QualityCode.Should().Be("A01");
        result[0].SourceMessageId.Should().Be("msg-002");
    }

    [Fact]
    public async Task Date_range_filtering_returns_subset()
    {
        var rows = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 10), "PT1H", 0.500m, "A03", "msg-001"),
            new(Utc(2025, 1, 2, 10), "PT1H", 0.600m, "A03", "msg-001"),
            new(Utc(2025, 1, 3, 10), "PT1H", 0.700m, "A03", "msg-001"),
        };

        await _sut.StoreTimeSeriesAsync("571313100000012345", rows, CancellationToken.None);

        var result = await _sut.GetConsumptionAsync(
            "571313100000012345",
            Utc(2025, 1, 2),
            Utc(2025, 1, 3),
            CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].QuantityKwh.Should().Be(0.600m);
    }
}
