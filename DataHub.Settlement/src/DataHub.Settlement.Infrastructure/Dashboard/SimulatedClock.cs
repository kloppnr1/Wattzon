using DataHub.Settlement.Domain;

namespace DataHub.Settlement.Infrastructure.Dashboard;

public sealed class SimulatedClock : IClock
{
    private static readonly DateOnly DefaultStart = new(2024, 12, 22);

    public DateOnly CurrentDate { get; private set; } = DefaultStart;

    DateOnly IClock.Today => CurrentDate;
    DateTime IClock.UtcNow => CurrentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public event Action<DateOnly>? DateChanged;

    public void AdvanceTo(DateOnly target)
    {
        if (target <= CurrentDate)
            throw new InvalidOperationException(
                $"Cannot move clock backward: current {CurrentDate}, requested {target}");

        CurrentDate = target;
        DateChanged?.Invoke(CurrentDate);
    }

    public void AdvanceDays(int days)
    {
        if (days <= 0)
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be positive");

        CurrentDate = CurrentDate.AddDays(days);
        DateChanged?.Invoke(CurrentDate);
    }

    public void Reset()
    {
        CurrentDate = DefaultStart;
        DateChanged?.Invoke(CurrentDate);
    }
}
