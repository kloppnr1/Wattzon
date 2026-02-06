using DataHub.Settlement.Infrastructure.Parsing;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class CimJsonParserTests
{
    private readonly CimJsonParser _sut = new();

    private static string LoadFixture(string name)
    {
        var path = Path.Combine(FindFixturesDir(), name);
        return File.ReadAllText(path);
    }

    private static string FindFixturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var fixtures = Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(fixtures))
                return fixtures;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find fixtures directory");
    }

    [Fact]
    public void Single_day_parses_24_hourly_points()
    {
        var json = LoadFixture("rsm012-single-day.json");

        var result = _sut.ParseRsm012(json);

        result.Should().HaveCount(1);
        var ts = result[0];
        ts.MeteringPointId.Should().Be("571313100000012345");
        ts.MeteringPointType.Should().Be("E17");
        ts.Resolution.Should().Be("PT1H");
        ts.Points.Should().HaveCount(24);
    }

    [Fact]
    public void Single_day_timestamps_are_computed_from_position()
    {
        var json = LoadFixture("rsm012-single-day.json");

        var result = _sut.ParseRsm012(json);
        var points = result[0].Points;

        points[0].Timestamp.Should().Be(DateTimeOffset.Parse("2025-01-01T00:00Z"));
        points[0].Position.Should().Be(1);
        points[5].Timestamp.Should().Be(DateTimeOffset.Parse("2025-01-01T05:00Z"));
        points[5].Position.Should().Be(6);
        points[23].Timestamp.Should().Be(DateTimeOffset.Parse("2025-01-01T23:00Z"));
        points[23].Position.Should().Be(24);
    }

    [Fact]
    public void Single_day_quantities_match_consumption_pattern()
    {
        var json = LoadFixture("rsm012-single-day.json");

        var points = _sut.ParseRsm012(json)[0].Points;

        // Night hours 0-5: 0.300
        points[0].QuantityKwh.Should().Be(0.300m);
        // Day hours 6-15: 0.500
        points[6].QuantityKwh.Should().Be(0.500m);
        // Peak hours 16-19: 1.200
        points[16].QuantityKwh.Should().Be(1.200m);
        // Late night 20-23: 0.400
        points[20].QuantityKwh.Should().Be(0.400m);
    }

    [Fact]
    public void Multi_day_parses_full_january()
    {
        var json = LoadFixture("rsm012-multi-day.json");

        var result = _sut.ParseRsm012(json);

        result.Should().HaveCount(1);
        result[0].Points.Should().HaveCount(744); // 31 days × 24 hours
        result[0].PeriodStart.Should().Be(DateTimeOffset.Parse("2025-01-01T00:00Z"));
        result[0].PeriodEnd.Should().Be(DateTimeOffset.Parse("2025-02-01T00:00Z"));
    }

    [Fact]
    public void Missing_quantity_uses_zero_with_A02_quality()
    {
        var json = LoadFixture("rsm012-missing-quantity.json");

        var result = _sut.ParseRsm012(json);
        var points = result[0].Points;

        points.Should().HaveCount(3);

        // Position 2 has quality A02 and no quantity
        points[1].QualityCode.Should().Be("A02");
        points[1].QuantityKwh.Should().Be(0m);

        // Position 1 and 3 have normal quantities
        points[0].QuantityKwh.Should().Be(0.300m);
        points[0].QualityCode.Should().Be("A03");
        points[2].QuantityKwh.Should().Be(0.500m);
        points[2].QualityCode.Should().Be("A01");
    }

    [Fact]
    public void Transaction_id_and_gsrn_are_extracted()
    {
        var json = LoadFixture("rsm012-single-day.json");

        var ts = _sut.ParseRsm012(json)[0];

        ts.TransactionId.Should().Be("txn-single-day-001");
        ts.MeteringPointId.Should().Be("571313100000012345");
    }
}
