using DataHub.Settlement.Application.Common;

namespace DataHub.Settlement.Application.Billing;

public interface IBillingRepository
{
    Task<PagedResult<BillingPeriodSummary>> GetBillingPeriodsAsync(int page, int pageSize, CancellationToken ct);
    Task<BillingPeriodDetail?> GetBillingPeriodAsync(Guid billingPeriodId, CancellationToken ct);
    Task<PagedResult<SettlementRunSummary>> GetSettlementRunsAsync(Guid? billingPeriodId, string? status, string? meteringPointId, string? gridAreaCode, DateOnly? fromDate, DateOnly? toDate, int page, int pageSize, CancellationToken ct);
    Task<SettlementRunDetail?> GetSettlementRunAsync(Guid settlementRunId, CancellationToken ct);
    Task<PagedResult<SettlementLineSummary>> GetSettlementLinesAsync(Guid settlementRunId, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<SettlementLineDetail>> GetSettlementLinesByMeteringPointAsync(string gsrn, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct);
    Task<CustomerBillingSummary?> GetCustomerBillingAsync(Guid customerId, CancellationToken ct);
}
