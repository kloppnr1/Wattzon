using DataHub.Settlement.Domain;

namespace DataHub.Settlement.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    public DateTime UtcNow => DateTime.UtcNow;
}
