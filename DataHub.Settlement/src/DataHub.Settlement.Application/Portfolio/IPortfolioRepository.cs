namespace DataHub.Settlement.Application.Portfolio;

public interface IPortfolioRepository
{
    Task<Customer> CreateCustomerAsync(string name, string cprCvr, string contactType, CancellationToken ct);
    Task<MeteringPoint> CreateMeteringPointAsync(MeteringPoint mp, CancellationToken ct);
    Task<Product> CreateProductAsync(string name, string energyModel, decimal marginOrePerKwh, decimal? supplementOrePerKwh, decimal subscriptionKrPerMonth, CancellationToken ct);
    Task<Contract> CreateContractAsync(Guid customerId, string gsrn, Guid productId, string billingFrequency, string paymentModel, DateOnly startDate, CancellationToken ct);
    Task<SupplyPeriod> CreateSupplyPeriodAsync(string gsrn, DateOnly startDate, CancellationToken ct);
    Task ActivateMeteringPointAsync(string gsrn, DateTime activatedAtUtc, CancellationToken ct);
    Task<Contract?> GetActiveContractAsync(string gsrn, CancellationToken ct);
    Task<Product?> GetProductAsync(Guid productId, CancellationToken ct);
    Task EnsureGridAreaAsync(string code, string gridOperatorGln, string gridOperatorName, string priceArea, CancellationToken ct);
    Task DeactivateMeteringPointAsync(string gsrn, DateTime deactivatedAtUtc, CancellationToken ct);
    Task EndSupplyPeriodAsync(string gsrn, DateOnly endDate, string endReason, CancellationToken ct);
    Task EndContractAsync(string gsrn, DateOnly endDate, CancellationToken ct);
    Task<IReadOnlyList<SupplyPeriod>> GetSupplyPeriodsAsync(string gsrn, CancellationToken ct);
    Task UpdateMeteringPointGridAreaAsync(string gsrn, string newGridAreaCode, string newPriceArea, CancellationToken ct);
    Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct);
    Task<Customer?> GetCustomerAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken ct);
    Task<PagedResult<Customer>> GetCustomersPagedAsync(int page, int pageSize, string? search, CancellationToken ct);
    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct);
    Task<IReadOnlyList<Contract>> GetContractsForCustomerAsync(Guid customerId, CancellationToken ct);
    Task<IReadOnlyList<MeteringPointWithSupply>> GetMeteringPointsForCustomerAsync(Guid customerId, CancellationToken ct);
}
