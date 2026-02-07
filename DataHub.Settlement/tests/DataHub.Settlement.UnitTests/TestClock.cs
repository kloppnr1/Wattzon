using DataHub.Settlement.Domain;

namespace DataHub.Settlement.UnitTests;

public sealed class TestClock : IClock
{
    public DateOnly Today { get; set; } = new(2025, 12, 31);
    public DateTime UtcNow => Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
