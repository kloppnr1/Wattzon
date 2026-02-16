namespace DataHub.Settlement.Infrastructure.Settlement;

/// <summary>
/// Calculates billing period boundaries based on frequency.
/// Weekly periods align to Danish weeks (Monday–Sunday).
/// All end dates are exclusive (the day after the last day of the period).
/// Example: January → periodStart=Jan 1, periodEnd=Feb 1.
/// </summary>
public static class BillingPeriodCalculator
{
    /// <summary>
    /// Returns the exclusive end date of the first billing period given a start date and frequency.
    /// The returned date is the day after the last day of the period.
    /// </summary>
    public static DateOnly GetFirstPeriodEnd(DateOnly startDate, string billingFrequency)
        => billingFrequency switch
        {
            "weekly" => GetWeekEnd(startDate),
            "monthly" => GetMonthEnd(startDate),
            "quarterly" => GetQuarterEnd(startDate),
            _ => throw new ArgumentException($"Unknown billing frequency: {billingFrequency}")
        };

    /// <summary>Day after the Sunday ending the Monday–Sunday week containing the given date (i.e., the next Monday).</summary>
    private static DateOnly GetWeekEnd(DateOnly date)
    {
        // DayOfWeek: Sunday=0 .. Saturday=6
        // Danish week: Monday=start, Sunday=end
        // daysUntilSunday: Monday→6, Tuesday→5, ... Saturday→1, Sunday→0
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(daysUntilSunday + 1); // +1 for exclusive end (Monday after Sunday)
    }

    /// <summary>First day of the month after the one containing the given date.</summary>
    private static DateOnly GetMonthEnd(DateOnly date)
        => new DateOnly(date.Year, date.Month, 1).AddMonths(1);

    /// <summary>First day of the quarter after the one containing the given date.</summary>
    private static DateOnly GetQuarterEnd(DateOnly date)
    {
        var quarterMonth = ((date.Month - 1) / 3 + 1) * 3;
        return new DateOnly(date.Year, quarterMonth, 1).AddMonths(1);
    }
}
