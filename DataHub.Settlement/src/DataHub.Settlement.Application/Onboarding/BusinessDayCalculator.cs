namespace DataHub.Settlement.Application.Onboarding;

/// <summary>
/// Calculates business days for the Danish electricity market.
/// Business days exclude weekends and Danish public holidays.
/// </summary>
public static class BusinessDayCalculator
{
    /// <summary>
    /// Returns the number of business days between two dates (exclusive of both endpoints).
    /// </summary>
    public static int CountBusinessDays(DateOnly from, DateOnly to)
    {
        if (to <= from) return 0;

        var count = 0;
        var current = from.AddDays(1);
        while (current < to)
        {
            if (IsBusinessDay(current))
                count++;
            current = current.AddDays(1);
        }
        return count;
    }

    /// <summary>
    /// Returns the earliest effective date that satisfies the minimum business day notice period.
    /// </summary>
    public static DateOnly EarliestEffectiveDate(DateOnly from, int minimumBusinessDays)
    {
        var current = from;
        var counted = 0;
        while (counted < minimumBusinessDays)
        {
            current = current.AddDays(1);
            if (IsBusinessDay(current))
                counted++;
        }
        // Effective date must itself be a business day
        while (!IsBusinessDay(current))
            current = current.AddDays(1);
        return current;
    }

    public static bool IsBusinessDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        return !IsDanishPublicHoliday(date);
    }

    /// <summary>
    /// Danish public holidays. Includes fixed holidays and moveable holidays based on Easter.
    /// </summary>
    public static bool IsDanishPublicHoliday(DateOnly date)
    {
        var year = date.Year;

        // Fixed holidays
        if (date == new DateOnly(year, 1, 1)) return true;   // Nytårsdag
        if (date == new DateOnly(year, 6, 5)) return true;   // Grundlovsdag
        if (date == new DateOnly(year, 12, 25)) return true;  // Juledag
        if (date == new DateOnly(year, 12, 26)) return true;  // 2. juledag

        // Easter-based moveable holidays
        var easter = CalculateEasterSunday(year);
        if (date == easter.AddDays(-3)) return true;  // Skærtorsdag
        if (date == easter.AddDays(-2)) return true;  // Langfredag
        if (date == easter) return true;               // Påskedag
        if (date == easter.AddDays(1)) return true;   // 2. påskedag
        if (date == easter.AddDays(26)) return true;  // Store bededag — NOTE: abolished from 2024, kept for historical calculations
        if (date == easter.AddDays(39)) return true;  // Kristi himmelfartsdag
        if (date == easter.AddDays(49)) return true;  // Pinsedag
        if (date == easter.AddDays(50)) return true;  // 2. pinsedag

        return false;
    }

    /// <summary>
    /// Gauss's Easter algorithm for a given year.
    /// </summary>
    private static DateOnly CalculateEasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
