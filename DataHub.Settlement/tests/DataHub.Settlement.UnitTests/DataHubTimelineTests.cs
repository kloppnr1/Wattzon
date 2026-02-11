using DataHub.Settlement.Infrastructure.Dashboard;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class DataHubTimelineTests
{
    private static readonly DateOnly Jan1 = new(2025, 1, 1);

    [Fact]
    public void ChangeOfSupplier_timeline_has_7_events()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        timeline.Events.Should().HaveCount(7);
    }

    [Fact]
    public void ChangeOfSupplier_submission_is_D_minus_7()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("Submit BRS-001").Should().Be(new DateOnly(2024, 12, 25));
    }

    [Fact]
    public void ChangeOfSupplier_acknowledgment_is_D_minus_6()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("DataHub Acknowledges").Should().Be(new DateOnly(2024, 12, 26));
    }

    [Fact]
    public void ChangeOfSupplier_RSM007_is_D_minus_2()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("Receive RSM-022").Should().Be(new DateOnly(2024, 12, 30));
    }

    [Fact]
    public void ChangeOfSupplier_effectuation_is_effective_date()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("Effectuation").Should().Be(Jan1);
    }

    [Fact]
    public void ChangeOfSupplier_rsm012_daily_starts_D_plus_1()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("RSM-012 Daily").Should().Be(new DateOnly(2025, 1, 2));
    }

    [Fact]
    public void ChangeOfSupplier_settlement_is_period_end_plus_2()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        // Period end = Feb 1, + 2 days = Feb 3
        timeline.GetDate("Run Settlement").Should().Be(new DateOnly(2025, 2, 3));
    }

    [Fact]
    public void ChangeOfSupplier_seed_data_is_D_minus_10()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("Seed Data").Should().Be(new DateOnly(2024, 12, 22));
    }

    [Fact]
    public void MoveIn_timeline_has_7_events()
    {
        var timeline = DataHubTimeline.BuildMoveInTimeline(Jan1);
        timeline.Events.Should().HaveCount(7);
    }

    [Fact]
    public void MoveIn_submission_is_D_minus_7()
    {
        var timeline = DataHubTimeline.BuildMoveInTimeline(Jan1);
        timeline.GetDate("Submit BRS-009").Should().Be(new DateOnly(2024, 12, 25));
    }

    [Fact]
    public void GetNextEvent_returns_first_future_event()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        var next = timeline.GetNextEvent(new DateOnly(2024, 12, 25));
        next.Should().NotBeNull();
        next!.Name.Should().Be("DataHub Acknowledges");
        next.Date.Should().Be(new DateOnly(2024, 12, 26));
    }

    [Fact]
    public void GetNextEvent_returns_null_after_all_events()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        var next = timeline.GetNextEvent(new DateOnly(2025, 3, 1));
        next.Should().BeNull();
    }

    [Fact]
    public void GetDate_returns_null_for_unknown_event()
    {
        var timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(Jan1);
        timeline.GetDate("NonExistent").Should().BeNull();
    }

    [Fact]
    public void Context_TotalMeteringDays_is_31_for_January()
    {
        var ctx = new ChangeOfSupplierContext("571313100000000001", "Test", Jan1);
        ctx.TotalMeteringDays.Should().Be(31);
    }

    [Fact]
    public void Context_TotalMeteringDays_is_28_for_February_non_leap()
    {
        var feb1 = new DateOnly(2025, 2, 1);
        var ctx = new MoveInContext("571313100000000001", "Test", feb1);
        ctx.TotalMeteringDays.Should().Be(28);
    }
}
