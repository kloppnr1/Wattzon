using DataHub.Settlement.Infrastructure.Billing;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class BillingPeriodCalculatorTests
{
    // ── Weekly: Monday–Sunday alignment, exclusive end = next Monday ──

    [Fact]
    public void Weekly_sunday_movein_first_period_ends_next_day()
    {
        // Move-in on Sunday Feb 15 2026 — that's the last day of the Mon–Sun week,
        // so the first billing period is just that single Sunday.
        // Exclusive end = Monday Feb 16.
        var moveIn = new DateOnly(2026, 2, 15); // Sunday
        moveIn.DayOfWeek.Should().Be(DayOfWeek.Sunday, "test assumes Sunday");

        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "weekly");

        periodEnd.Should().Be(new DateOnly(2026, 2, 16), "exclusive end is Monday after the single Sunday");
        periodEnd.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Weekly_sunday_movein_invoice_is_due_next_day()
    {
        // Period covers only Sunday. Exclusive end = Monday.
        // Invoice is due when periodEnd <= today, i.e., on Monday.
        var moveIn = new DateOnly(2026, 2, 15); // Sunday
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "weekly");

        InvoicingService.IsPeriodDue("weekly", periodEnd, moveIn)
            .Should().BeFalse("period hasn't ended yet on Sunday");

        InvoicingService.IsPeriodDue("weekly", periodEnd, new DateOnly(2026, 2, 16))
            .Should().BeTrue("invoice due on Monday (exclusive end)");
    }

    [Fact]
    public void Weekly_monday_movein_first_period_ends_next_monday()
    {
        // Monday Feb 16 2026 — full week ahead, period ends exclusive on Monday Feb 23
        var moveIn = new DateOnly(2026, 2, 16); // Monday
        moveIn.DayOfWeek.Should().Be(DayOfWeek.Monday);

        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "weekly");

        periodEnd.Should().Be(new DateOnly(2026, 2, 23));
        periodEnd.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Weekly_wednesday_movein_first_period_ends_next_monday()
    {
        // Wednesday Feb 18 2026 — period ends exclusive on Monday Feb 23
        var moveIn = new DateOnly(2026, 2, 18); // Wednesday
        moveIn.DayOfWeek.Should().Be(DayOfWeek.Wednesday);

        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "weekly");

        periodEnd.Should().Be(new DateOnly(2026, 2, 23));
        periodEnd.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Weekly_saturday_movein_first_period_ends_monday()
    {
        // Saturday Feb 14 2026 — period ends Sunday Feb 15, exclusive end = Monday Feb 16
        var moveIn = new DateOnly(2026, 2, 14); // Saturday
        moveIn.DayOfWeek.Should().Be(DayOfWeek.Saturday);

        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "weekly");

        periodEnd.Should().Be(new DateOnly(2026, 2, 16));
        periodEnd.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Weekly_monday_movein_invoice_not_due_until_next_monday()
    {
        var moveIn = new DateOnly(2026, 2, 16); // Monday
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "weekly");

        // Friday during the week — not due yet
        InvoicingService.IsPeriodDue("weekly", periodEnd, new DateOnly(2026, 2, 20))
            .Should().BeFalse("period hasn't ended yet");

        // Sunday — still not due (exclusive end is Monday)
        InvoicingService.IsPeriodDue("weekly", periodEnd, new DateOnly(2026, 2, 22))
            .Should().BeFalse("exclusive end is Monday, not Sunday");

        // Monday — due
        InvoicingService.IsPeriodDue("weekly", periodEnd, new DateOnly(2026, 2, 23))
            .Should().BeTrue("period ends on Monday (exclusive)");
    }

    // ── Monthly (exclusive: first day of next month) ──

    [Fact]
    public void Monthly_mid_month_movein_first_period_ends_at_next_month()
    {
        var moveIn = new DateOnly(2026, 2, 15);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "monthly");

        periodEnd.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Monthly_first_day_first_period_ends_at_next_month()
    {
        var moveIn = new DateOnly(2026, 3, 1);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "monthly");

        periodEnd.Should().Be(new DateOnly(2026, 4, 1));
    }

    // ── Quarterly (exclusive: first day of next quarter) ──

    [Fact]
    public void Quarterly_mid_quarter_movein_first_period_ends_at_next_quarter()
    {
        var moveIn = new DateOnly(2026, 2, 15);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "quarterly");

        periodEnd.Should().Be(new DateOnly(2026, 4, 1), "Q1 ends exclusive on Apr 1");
    }

    [Fact]
    public void Quarterly_q2_movein_first_period_ends_jul_1()
    {
        var moveIn = new DateOnly(2026, 5, 10);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(moveIn, "quarterly");

        periodEnd.Should().Be(new DateOnly(2026, 7, 1));
    }

    // ── Monthly edge cases ──

    [Fact]
    public void Monthly_last_day_of_month_returns_first_of_next()
    {
        var start = new DateOnly(2025, 1, 31);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "monthly");
        periodEnd.Should().Be(new DateOnly(2025, 2, 1));
    }

    [Fact]
    public void Monthly_jan1_returns_feb1()
    {
        var start = new DateOnly(2025, 1, 1);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "monthly");
        periodEnd.Should().Be(new DateOnly(2025, 2, 1));
    }

    [Fact]
    public void Monthly_dec31_returns_jan1_next_year()
    {
        var start = new DateOnly(2025, 12, 31);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "monthly");
        periodEnd.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void Monthly_feb28_in_leap_year_returns_mar1()
    {
        var start = new DateOnly(2024, 2, 28); // 2024 is a leap year
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "monthly");
        periodEnd.Should().Be(new DateOnly(2024, 3, 1));
    }

    // ── Quarterly edge cases ──

    [Fact]
    public void Quarterly_mar31_returns_apr1()
    {
        var start = new DateOnly(2025, 3, 31);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "quarterly");
        periodEnd.Should().Be(new DateOnly(2025, 4, 1));
    }

    [Fact]
    public void Quarterly_jan1_returns_apr1()
    {
        var start = new DateOnly(2025, 1, 1);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "quarterly");
        periodEnd.Should().Be(new DateOnly(2025, 4, 1));
    }

    // ── Days in period verification ──

    [Fact]
    public void Exclusive_end_gives_correct_day_count()
    {
        // Weekly: Monday to next Monday = 7 days
        var weekStart = new DateOnly(2025, 1, 6); // Monday
        var weekEnd = BillingPeriodCalculator.GetFirstPeriodEnd(weekStart, "weekly");
        (weekEnd.DayNumber - weekStart.DayNumber).Should().Be(7);

        // Monthly: Jan 1 to Feb 1 = 31 days
        var monthStart = new DateOnly(2025, 1, 1);
        var monthEnd = BillingPeriodCalculator.GetFirstPeriodEnd(monthStart, "monthly");
        (monthEnd.DayNumber - monthStart.DayNumber).Should().Be(31);

        // Sunday move-in: Sunday to Monday = 1 day
        var sundayStart = new DateOnly(2025, 1, 5); // Sunday
        var sundayEnd = BillingPeriodCalculator.GetFirstPeriodEnd(sundayStart, "weekly");
        (sundayEnd.DayNumber - sundayStart.DayNumber).Should().Be(1);
    }

    // ── Daily (exclusive: next day) ──

    [Fact]
    public void Daily_returns_next_day()
    {
        var start = new DateOnly(2025, 1, 15);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "daily");
        periodEnd.Should().Be(new DateOnly(2025, 1, 16));
    }

    [Fact]
    public void Daily_end_of_month()
    {
        var start = new DateOnly(2025, 1, 31);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "daily");
        periodEnd.Should().Be(new DateOnly(2025, 2, 1));
    }

    [Fact]
    public void Daily_exclusive_end_gives_1_day()
    {
        var start = new DateOnly(2025, 3, 15);
        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(start, "daily");
        (periodEnd.DayNumber - start.DayNumber).Should().Be(1);
    }

    // ── Invalid frequency ──

    [Fact]
    public void Throws_on_unknown_frequency()
    {
        var act = () => BillingPeriodCalculator.GetFirstPeriodEnd(new DateOnly(2026, 1, 1), "biweekly");

        act.Should().Throw<ArgumentException>().WithMessage("*biweekly*");
    }
}
