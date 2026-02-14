namespace DataHub.Settlement.Application.Settlement;

public interface ISettlementResultStore
{
    Task StoreAsync(string gsrn, string gridAreaCode, SettlementResult result, string billingFrequency, CancellationToken ct);
}
