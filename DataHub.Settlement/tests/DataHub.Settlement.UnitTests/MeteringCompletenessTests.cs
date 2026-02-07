using DataHub.Settlement.Application.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class MeteringCompletenessTests
{
    [Fact]
    public void Complete_when_received_equals_expected()
    {
        var result = new MeteringCompleteness(ExpectedHours: 744, ReceivedHours: 744, IsComplete: true);

        result.IsComplete.Should().BeTrue();
        result.ExpectedHours.Should().Be(744);
        result.ReceivedHours.Should().Be(744);
    }

    [Fact]
    public void Incomplete_when_received_less_than_expected()
    {
        var result = new MeteringCompleteness(ExpectedHours: 744, ReceivedHours: 500, IsComplete: false);

        result.IsComplete.Should().BeFalse();
        result.ReceivedHours.Should().Be(500);
    }

    [Fact]
    public void Incomplete_when_zero_readings()
    {
        var result = new MeteringCompleteness(ExpectedHours: 24, ReceivedHours: 0, IsComplete: false);

        result.IsComplete.Should().BeFalse();
        result.ReceivedHours.Should().Be(0);
    }

    [Fact]
    public void Complete_when_received_exceeds_expected()
    {
        // Edge case: more readings than expected (e.g., DST or corrections)
        var result = new MeteringCompleteness(ExpectedHours: 24, ReceivedHours: 25, IsComplete: true);

        result.IsComplete.Should().BeTrue();
    }
}
