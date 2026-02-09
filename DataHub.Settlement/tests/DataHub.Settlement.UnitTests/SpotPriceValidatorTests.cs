using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class SpotPriceValidatorTests
{
    private readonly SpotPriceValidator _sut = new();

    [Fact]
    public void All_hours_present_returns_valid()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 1, 3, 0, 0, DateTimeKind.Utc);
        var prices = new List<SpotPriceRow>
        {
            new("DK1", start, 50m),
            new("DK1", start.AddHours(1), 55m),
            new("DK1", start.AddHours(2), 60m),
        };

        var result = _sut.Validate(prices, start, end);

        result.IsValid.Should().BeTrue();
        result.MissingSlots.Should().BeEmpty();
    }

    [Fact]
    public void Missing_hours_returns_invalid_with_list()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 1, 4, 0, 0, DateTimeKind.Utc);
        var prices = new List<SpotPriceRow>
        {
            new("DK1", start, 50m),
            // hour 1 missing
            new("DK1", start.AddHours(2), 60m),
            // hour 3 missing
        };

        var result = _sut.Validate(prices, start, end);

        result.IsValid.Should().BeFalse();
        result.MissingSlots.Should().HaveCount(2);
        result.MissingSlots.Should().Contain(start.AddHours(1));
        result.MissingSlots.Should().Contain(start.AddHours(3));
    }

    [Fact]
    public void No_prices_at_all_returns_all_hours_missing()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var prices = new List<SpotPriceRow>();

        var result = _sut.Validate(prices, start, end);

        result.IsValid.Should().BeFalse();
        result.MissingSlots.Should().HaveCount(24);
    }

    [Fact]
    public void Empty_period_returns_valid()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start; // zero-length period
        var prices = new List<SpotPriceRow>();

        var result = _sut.Validate(prices, start, end);

        result.IsValid.Should().BeTrue();
        result.MissingSlots.Should().BeEmpty();
    }

    [Fact]
    public void Full_month_with_all_prices_returns_valid()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var prices = Enumerable.Range(0, 744)
            .Select(h => new SpotPriceRow("DK1", start.AddHours(h), 50m + h % 10))
            .ToList();

        var result = _sut.Validate(prices, start, end);

        result.IsValid.Should().BeTrue();
        result.MissingSlots.Should().BeEmpty();
    }
}
