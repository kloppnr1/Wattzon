using DataHub.Settlement.Infrastructure.Dashboard;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class SimulatedClockTests
{
    [Fact]
    public void Default_start_date_is_Dec_22_2024()
    {
        var clock = new SimulatedClock();
        clock.CurrentDate.Should().Be(new DateOnly(2024, 12, 22));
    }

    [Fact]
    public void AdvanceDays_moves_forward()
    {
        var clock = new SimulatedClock();
        clock.AdvanceDays(3);
        clock.CurrentDate.Should().Be(new DateOnly(2024, 12, 25));
    }

    [Fact]
    public void AdvanceTo_moves_to_exact_date()
    {
        var clock = new SimulatedClock();
        clock.AdvanceTo(new DateOnly(2025, 1, 1));
        clock.CurrentDate.Should().Be(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void AdvanceTo_backward_throws()
    {
        var clock = new SimulatedClock();
        clock.AdvanceTo(new DateOnly(2025, 1, 1));

        var act = () => clock.AdvanceTo(new DateOnly(2024, 12, 30));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*backward*");
    }

    [Fact]
    public void AdvanceTo_same_date_throws()
    {
        var clock = new SimulatedClock();
        var act = () => clock.AdvanceTo(new DateOnly(2024, 12, 22));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AdvanceDays_zero_throws()
    {
        var clock = new SimulatedClock();
        var act = () => clock.AdvanceDays(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AdvanceDays_negative_throws()
    {
        var clock = new SimulatedClock();
        var act = () => clock.AdvanceDays(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Reset_returns_to_default()
    {
        var clock = new SimulatedClock();
        clock.AdvanceDays(10);
        clock.Reset();
        clock.CurrentDate.Should().Be(new DateOnly(2024, 12, 22));
    }

    [Fact]
    public void DateChanged_fires_on_advance()
    {
        var clock = new SimulatedClock();
        DateOnly? received = null;
        clock.DateChanged += d => received = d;

        clock.AdvanceDays(1);
        received.Should().Be(new DateOnly(2024, 12, 23));
    }

    [Fact]
    public void DateChanged_fires_on_reset()
    {
        var clock = new SimulatedClock();
        clock.AdvanceDays(5);

        DateOnly? received = null;
        clock.DateChanged += d => received = d;

        clock.Reset();
        received.Should().Be(new DateOnly(2024, 12, 22));
    }
}
