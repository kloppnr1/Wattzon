using DataHub.Settlement.Infrastructure.Billing;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class InvoicingServiceTests
{
    [Fact]
    public void Skips_runs_when_monthly_period_not_yet_due()
    {
        // Period ends Feb 28, today is Feb 15 — not due yet
        var periodEnd = new DateOnly(2025, 2, 28);
        var today = new DateOnly(2025, 2, 15);

        InvoicingService.IsPeriodDue("monthly", periodEnd, today).Should().BeFalse();
    }

    [Fact]
    public void Creates_invoice_when_monthly_period_due()
    {
        // Period ends Jan 31, today is Feb 1 — due
        var periodEnd = new DateOnly(2025, 1, 31);
        var today = new DateOnly(2025, 2, 1);

        InvoicingService.IsPeriodDue("monthly", periodEnd, today).Should().BeTrue();
    }

    [Fact]
    public void Creates_invoice_when_monthly_period_end_equals_today()
    {
        // Period ends Jan 31, today is Jan 31 — due (periodEnd <= today)
        var periodEnd = new DateOnly(2025, 1, 31);
        var today = new DateOnly(2025, 1, 31);

        InvoicingService.IsPeriodDue("monthly", periodEnd, today).Should().BeTrue();
    }

    [Fact]
    public void Creates_invoice_when_quarterly_period_due()
    {
        // Q1 period ends March 15, quarter end is March 31, today is April 1 — due
        var periodEnd = new DateOnly(2025, 3, 15);
        var today = new DateOnly(2025, 4, 1);

        InvoicingService.IsPeriodDue("quarterly", periodEnd, today).Should().BeTrue();
    }

    [Fact]
    public void Skips_when_quarterly_period_not_yet_due()
    {
        // Q1 period ends Feb 28, quarter end is March 31, today is March 15 — not due
        var periodEnd = new DateOnly(2025, 2, 28);
        var today = new DateOnly(2025, 3, 15);

        InvoicingService.IsPeriodDue("quarterly", periodEnd, today).Should().BeFalse();
    }

    [Fact]
    public void Quarterly_q2_due_after_june_30()
    {
        var periodEnd = new DateOnly(2025, 5, 31);
        var today = new DateOnly(2025, 7, 1);

        InvoicingService.IsPeriodDue("quarterly", periodEnd, today).Should().BeTrue();
    }

    [Fact]
    public void Quarterly_q4_due_after_dec_31()
    {
        var periodEnd = new DateOnly(2025, 11, 30);
        var today = new DateOnly(2026, 1, 1);

        InvoicingService.IsPeriodDue("quarterly", periodEnd, today).Should().BeTrue();
    }

    [Fact]
    public void Quarterly_not_due_on_quarter_end_day()
    {
        // Quarter end is March 31, today is March 31 — not due (today > quarterEnd required)
        var periodEnd = new DateOnly(2025, 3, 15);
        var today = new DateOnly(2025, 3, 31);

        InvoicingService.IsPeriodDue("quarterly", periodEnd, today).Should().BeFalse();
    }

    [Fact]
    public void Weekly_due_after_sunday()
    {
        // Week Mon Jan 6 – Sun Jan 12, today is Mon Jan 13 — due
        var periodEnd = new DateOnly(2025, 1, 12); // Sunday
        var today = new DateOnly(2025, 1, 13);

        InvoicingService.IsPeriodDue("weekly", periodEnd, today).Should().BeTrue();
    }

    [Fact]
    public void Weekly_due_on_sunday()
    {
        // Week Mon Jan 6 – Sun Jan 12, today is Sun Jan 12 — due (periodEnd <= today)
        var periodEnd = new DateOnly(2025, 1, 12); // Sunday
        var today = new DateOnly(2025, 1, 12);

        InvoicingService.IsPeriodDue("weekly", periodEnd, today).Should().BeTrue();
    }

    [Fact]
    public void Weekly_not_due_before_period_end()
    {
        // Week Mon Jan 6 – Sun Jan 12, today is Fri Jan 10 — not due
        var periodEnd = new DateOnly(2025, 1, 12); // Sunday
        var today = new DateOnly(2025, 1, 10);

        InvoicingService.IsPeriodDue("weekly", periodEnd, today).Should().BeFalse();
    }
}
