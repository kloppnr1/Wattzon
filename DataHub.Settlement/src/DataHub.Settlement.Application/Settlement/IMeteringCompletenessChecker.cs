namespace DataHub.Settlement.Application.Settlement;

public record MeteringCompleteness(int ExpectedHours, int ReceivedHours, bool IsComplete);

public interface IMeteringCompletenessChecker
{
    Task<MeteringCompleteness> CheckAsync(string gsrn, DateTime periodStart, DateTime periodEnd, CancellationToken ct);
}
