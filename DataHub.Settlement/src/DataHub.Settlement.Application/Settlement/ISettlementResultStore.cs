namespace DataHub.Settlement.Application.Settlement;

public interface ISettlementResultStore
{
    Task StoreAsync(string gsrn, string gridAreaCode, SettlementResult result, string billingFrequency, CancellationToken ct);
    Task<bool> HasSettlementRunAsync(string gsrn, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
}
