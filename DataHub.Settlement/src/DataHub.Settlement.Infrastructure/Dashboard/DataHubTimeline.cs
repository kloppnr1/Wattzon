namespace DataHub.Settlement.Infrastructure.Dashboard;

public record TimelineEvent(string Name, DateOnly Date, string Description);

public record ProcessTimeline(IReadOnlyList<TimelineEvent> Events)
{
    public DateOnly? GetDate(string eventName) =>
        Events.FirstOrDefault(e => e.Name == eventName)?.Date;

    public TimelineEvent? GetNextEvent(DateOnly currentDate) =>
        Events.Where(e => e.Date > currentDate).OrderBy(e => e.Date).FirstOrDefault();
}

public static class DataHubTimeline
{
    public static ProcessTimeline BuildChangeOfSupplierTimeline(DateOnly effectiveDate)
    {
        // D = effective date (always 1st of month)
        // BRS-001 submitted at D-7
        // Acknowledgment at D-6 (submission + 1 day)
        // RSM-007 at D-2
        // Effectuation at D
        // RSM-012 daily from D+1 through period end + 1
        // Metering complete at period end + 2 days
        // Settlement eligible = metering complete

        var submission = effectiveDate.AddDays(-7);
        var acknowledgment = submission.AddDays(1);
        var rsm007 = effectiveDate.AddDays(-2);
        var periodEnd = effectiveDate.AddMonths(1);
        var meteringComplete = periodEnd.AddDays(2);

        var events = new List<TimelineEvent>
        {
            new("Seed Data", submission.AddDays(-3), "Seed reference data and create customer"),
            new("Submit BRS-001", submission, $"Submit change of supplier request (D-7 from {effectiveDate})"),
            new("DataHub Acknowledges", acknowledgment, "DataHub processes and acknowledges the request"),
            new("Receive RSM-007", rsm007, "Master data received, metering point activated"),
            new("Effectuation", effectiveDate, "Supply begins, process completed"),
            new("RSM-012 Daily", effectiveDate.AddDays(1), $"Daily metering deliveries for {effectiveDate:MMM yyyy}"),
            new("Run Settlement", meteringComplete, "Settlement calculation and billing"),
        };

        return new ProcessTimeline(events);
    }

    public static ProcessTimeline BuildMoveInTimeline(DateOnly effectiveDate)
    {
        var submission = effectiveDate.AddDays(-7);
        var acknowledgment = submission.AddDays(1);
        var rsm007 = effectiveDate.AddDays(-2);
        var periodEnd = effectiveDate.AddMonths(1);
        var meteringComplete = periodEnd.AddDays(2);

        var events = new List<TimelineEvent>
        {
            new("Seed Data", submission.AddDays(-3), "Seed reference data and create customer"),
            new("Submit BRS-009", submission, $"Submit move-in request (D-7 from {effectiveDate})"),
            new("DataHub Acknowledges", acknowledgment, "DataHub processes and acknowledges the request"),
            new("Receive RSM-007", rsm007, "Master data received, supply period created, metering point activated"),
            new("Effectuation", effectiveDate, "Supply begins, process completed"),
            new("RSM-012 Daily", effectiveDate.AddDays(1), $"Daily metering deliveries for {effectiveDate:MMM yyyy}"),
            new("Run Settlement", meteringComplete, "Settlement calculation and billing"),
        };

        return new ProcessTimeline(events);
    }
}
