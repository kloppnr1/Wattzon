using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Infrastructure.Metering;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class MeteringDataHistoryTests
{
    private readonly MeteringDataRepository _sut = new(TestDatabase.ConnectionString);

    public MeteringDataHistoryTests(TestDatabase db) { }

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task First_store_creates_no_history()
    {
        var rows = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.300m, "A03", "msg-001"),
            new(Utc(2025, 1, 1, 1), "PT1H", 0.400m, "A03", "msg-001"),
        };

        var changedCount = await _sut.StoreTimeSeriesWithHistoryAsync("571313100000099999", rows, CancellationToken.None);

        changedCount.Should().Be(0);

        var changes = await _sut.GetChangesAsync("571313100000099999", Utc(2025, 1, 1), Utc(2025, 1, 2), CancellationToken.None);
        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Revised_data_creates_history_entries()
    {
        var original = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.300m, "A03", "msg-001"),
            new(Utc(2025, 1, 1, 1), "PT1H", 0.400m, "A03", "msg-001"),
        };
        await _sut.StoreTimeSeriesWithHistoryAsync("571313100000088888", original, CancellationToken.None);

        var revised = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.350m, "A01", "msg-002"), // changed
            new(Utc(2025, 1, 1, 1), "PT1H", 0.400m, "A01", "msg-002"), // same quantity
        };
        var changedCount = await _sut.StoreTimeSeriesWithHistoryAsync("571313100000088888", revised, CancellationToken.None);

        changedCount.Should().Be(1);

        var changes = await _sut.GetChangesAsync("571313100000088888", Utc(2025, 1, 1), Utc(2025, 1, 2), CancellationToken.None);
        changes.Should().HaveCount(1);
        changes[0].PreviousKwh.Should().Be(0.300m);
        changes[0].NewKwh.Should().Be(0.350m);
        changes[0].PreviousMessageId.Should().Be("msg-001");
        changes[0].NewMessageId.Should().Be("msg-002");
    }

    [Fact]
    public async Task Multiple_revisions_create_multiple_history_rows()
    {
        var v1 = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.300m, "A03", "msg-001"),
        };
        await _sut.StoreTimeSeriesWithHistoryAsync("571313100000077777", v1, CancellationToken.None);

        var v2 = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.350m, "A01", "msg-002"),
        };
        await _sut.StoreTimeSeriesWithHistoryAsync("571313100000077777", v2, CancellationToken.None);

        var v3 = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.400m, "A01", "msg-003"),
        };
        await _sut.StoreTimeSeriesWithHistoryAsync("571313100000077777", v3, CancellationToken.None);

        var changes = await _sut.GetChangesAsync("571313100000077777", Utc(2025, 1, 1), Utc(2025, 1, 2), CancellationToken.None);
        changes.Should().HaveCount(2);
        changes[0].PreviousKwh.Should().Be(0.300m);
        changes[0].NewKwh.Should().Be(0.350m);
        changes[1].PreviousKwh.Should().Be(0.350m);
        changes[1].NewKwh.Should().Be(0.400m);
    }

    [Fact]
    public async Task GetChanges_filters_by_date_range()
    {
        var original = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.300m, "A03", "msg-001"),
            new(Utc(2025, 1, 2, 0), "PT1H", 0.300m, "A03", "msg-001"),
        };
        await _sut.StoreTimeSeriesWithHistoryAsync("571313100000066666", original, CancellationToken.None);

        var revised = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.500m, "A01", "msg-002"),
            new(Utc(2025, 1, 2, 0), "PT1H", 0.600m, "A01", "msg-002"),
        };
        await _sut.StoreTimeSeriesWithHistoryAsync("571313100000066666", revised, CancellationToken.None);

        // Only query Jan 2 range
        var changes = await _sut.GetChangesAsync("571313100000066666", Utc(2025, 1, 2), Utc(2025, 1, 3), CancellationToken.None);
        changes.Should().HaveCount(1);
        changes[0].NewKwh.Should().Be(0.600m);
    }

    [Fact]
    public async Task Unchanged_quantity_does_not_create_history()
    {
        var original = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.300m, "A03", "msg-001"),
        };
        await _sut.StoreTimeSeriesWithHistoryAsync("571313100000055555", original, CancellationToken.None);

        // Same quantity, different quality code and message — should NOT create history
        var sameQuantity = new List<MeteringDataRow>
        {
            new(Utc(2025, 1, 1, 0), "PT1H", 0.300m, "A01", "msg-002"),
        };
        var changedCount = await _sut.StoreTimeSeriesWithHistoryAsync("571313100000055555", sameQuantity, CancellationToken.None);

        changedCount.Should().Be(0);
    }
}
