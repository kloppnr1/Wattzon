using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Infrastructure.Parsing;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class ReconciliationTests
{
    private readonly ReconciliationService _sut = new();

    [Fact]
    public void Matching_data_returns_reconciled()
    {
        var hour1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hour2 = new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc);

        var datahub = new Rsm014Aggregation("344",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2),
            1.000m,
            new[] { new AggregationPoint(hour1, 0.500m), new AggregationPoint(hour2, 0.500m) });

        var own = new[]
        {
            new AggregationPoint(hour1, 0.500m),
            new AggregationPoint(hour2, 0.500m),
        };

        var result = _sut.Reconcile(datahub, own);

        result.IsReconciled.Should().BeTrue();
        result.Discrepancies.Should().BeEmpty();
        result.DiscrepancyKwh.Should().Be(0m);
    }

    [Fact]
    public void Discrepancy_detected_when_values_differ()
    {
        var hour1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hour2 = new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc);

        var datahub = new Rsm014Aggregation("344",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2),
            1.000m,
            new[] { new AggregationPoint(hour1, 0.500m), new AggregationPoint(hour2, 0.500m) });

        var own = new[]
        {
            new AggregationPoint(hour1, 0.600m), // +0.100 discrepancy
            new AggregationPoint(hour2, 0.500m),
        };

        var result = _sut.Reconcile(datahub, own);

        result.IsReconciled.Should().BeFalse();
        result.Discrepancies.Should().HaveCount(1);
        result.Discrepancies[0].DeltaKwh.Should().Be(0.100m);
        result.DiscrepancyKwh.Should().Be(0.100m);
    }

    [Fact]
    public void Missing_own_data_shows_as_negative_discrepancy()
    {
        var hour1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hour2 = new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc);

        var datahub = new Rsm014Aggregation("344",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2),
            1.000m,
            new[] { new AggregationPoint(hour1, 0.500m), new AggregationPoint(hour2, 0.500m) });

        var own = new[]
        {
            new AggregationPoint(hour1, 0.500m),
            // hour2 missing
        };

        var result = _sut.Reconcile(datahub, own);

        result.IsReconciled.Should().BeFalse();
        result.Discrepancies.Should().HaveCount(1);
        result.Discrepancies[0].Timestamp.Should().Be(hour2);
        result.Discrepancies[0].OwnKwh.Should().Be(0m);
        result.Discrepancies[0].DataHubKwh.Should().Be(0.500m);
    }

    [Fact]
    public void Rsm014_parser_extracts_aggregation()
    {
        var json = """
        {
            "MarketDocument": {
                "mRID": "rsm014-test",
                "Series": [
                    {
                        "marketEvaluationPoint": {
                            "meteringGridArea": { "mRID": "344" }
                        },
                        "Period": {
                            "resolution": "PT1H",
                            "timeInterval": {
                                "start": "2025-01-01T00:00:00Z",
                                "end": "2025-01-01T03:00:00Z"
                            },
                            "Point": [
                                { "position": 1, "quantity": 100.5 },
                                { "position": 2, "quantity": 200.3 },
                                { "position": 3, "quantity": 150.0 }
                            ]
                        }
                    }
                ]
            }
        }
        """;

        var parser = new CimJsonParser();
        var result = parser.ParseRsm014(json);

        result.GridAreaCode.Should().Be("344");
        result.PeriodStart.Should().Be(new DateOnly(2025, 1, 1));
        result.PeriodEnd.Should().Be(new DateOnly(2025, 1, 1));
        result.TotalKwh.Should().Be(450.8m);
        result.Points.Should().HaveCount(3);
        result.Points[0].QuantityKwh.Should().Be(100.5m);
    }

    [Fact]
    public void Small_difference_within_tolerance_is_reconciled()
    {
        var hour1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var datahub = new Rsm014Aggregation("344",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2),
            0.500m,
            new[] { new AggregationPoint(hour1, 0.500m) });

        var own = new[]
        {
            new AggregationPoint(hour1, 0.5005m), // within 0.001 tolerance
        };

        var result = _sut.Reconcile(datahub, own);

        result.IsReconciled.Should().BeTrue();
    }
}
