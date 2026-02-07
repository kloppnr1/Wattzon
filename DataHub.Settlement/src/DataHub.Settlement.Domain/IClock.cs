namespace DataHub.Settlement.Domain;

public interface IClock
{
    DateOnly Today { get; }
    DateTime UtcNow { get; }
}
