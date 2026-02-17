namespace DataHub.Settlement.Application.Settlement;

public interface ISettlementResultStore
{
    Task StoreAsync(string gsrn, string gridAreaCode, SettlementResult result, string billingFrequency, CancellationToken ct);
    Task<bool> HasSettlementRunAsync(string gsrn, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
    Task<IReadOnlyList<AffectedSettlementPeriod>> GetAffectedSettlementPeriodsAsync(string gsrn, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task StoreFailedRunAsync(string gsrn, string gridAreaCode, DateOnly periodStart, DateOnly periodEnd, string errorDetails, CancellationToken ct);
}
