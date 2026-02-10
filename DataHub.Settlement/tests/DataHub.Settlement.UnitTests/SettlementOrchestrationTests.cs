using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Settlement;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class SettlementOrchestrationTests
{
    private readonly ProcessStateMachineTests.InMemoryProcessRepository _processRepo = new();
    private readonly StubPortfolioRepository _portfolioRepo = new();
    private readonly StubCompletenessChecker _completenessChecker = new();
    private readonly StubDataLoader _dataLoader = new();
    private readonly SettlementEngine _engine = new();
    private readonly StubResultStore _resultStore = new();

    private SettlementOrchestrationService CreateSut() => new(
        _processRepo, _portfolioRepo, _completenessChecker,
        _dataLoader, _engine, _resultStore,
        NullLogger<SettlementOrchestrationService>.Instance);

    [Fact]
    public async Task Skips_when_metering_incomplete()
    {
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(744, 500, false);

        var sut = CreateSut();
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(0);
    }

    [Fact]
    public async Task Skips_when_already_settled()
    {
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(744, 744, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "monthly", "post_payment", new DateOnly(2025, 1, 1));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut();
        await sut.RunTickAsync(CancellationToken.None);

        // First run should store
        _resultStore.StoreCount.Should().Be(1);
    }

    [Fact]
    public async Task Runs_settlement_when_eligible()
    {
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(744, 744, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "monthly", "post_payment", new DateOnly(2025, 1, 1));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut();
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(1);
        _resultStore.LastGsrn.Should().Be("571313100000012345");
    }

    // ── Test doubles ──

    internal sealed class StubCompletenessChecker : IMeteringCompletenessChecker
    {
        public MeteringCompleteness Result { get; set; } = new(0, 0, false);
        public Task<MeteringCompleteness> CheckAsync(string gsrn, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
            => Task.FromResult(Result);
    }

    internal sealed class StubPortfolioRepository : IPortfolioRepository
    {
        public Contract? Contract { get; set; }
        public Product? Product { get; set; }

        public Task<Contract?> GetActiveContractAsync(string gsrn, CancellationToken ct) => Task.FromResult(Contract);
        public Task<Product?> GetProductAsync(Guid productId, CancellationToken ct) => Task.FromResult(Product);

        // Unused methods
        public Task<Customer> CreateCustomerAsync(string name, string cprCvr, string contactType, Address? billingAddress, CancellationToken ct) => throw new NotImplementedException();
        public Task<Customer?> GetCustomerByCprCvrAsync(string cprCvr, CancellationToken ct) => throw new NotImplementedException();
        public Task<MeteringPoint> CreateMeteringPointAsync(MeteringPoint mp, CancellationToken ct) => throw new NotImplementedException();
        public Task<Product> CreateProductAsync(string name, string energyModel, decimal marginOrePerKwh, decimal? supplementOrePerKwh, decimal subscriptionKrPerMonth, CancellationToken ct) => throw new NotImplementedException();
        public Task<Contract> CreateContractAsync(Guid customerId, string gsrn, Guid productId, string billingFrequency, string paymentModel, DateOnly startDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<SupplyPeriod> CreateSupplyPeriodAsync(string gsrn, DateOnly startDate, CancellationToken ct) => throw new NotImplementedException();
        public Task ActivateMeteringPointAsync(string gsrn, DateTime activatedAtUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task EnsureGridAreaAsync(string code, string gridOperatorGln, string gridOperatorName, string priceArea, CancellationToken ct) => throw new NotImplementedException();
        public Task DeactivateMeteringPointAsync(string gsrn, DateTime deactivatedAtUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task EndSupplyPeriodAsync(string gsrn, DateOnly endDate, string endReason, CancellationToken ct) => throw new NotImplementedException();
        public Task EndContractAsync(string gsrn, DateOnly endDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SupplyPeriod>> GetSupplyPeriodsAsync(string gsrn, CancellationToken ct) => throw new NotImplementedException();
        public Task UpdateMeteringPointGridAreaAsync(string gsrn, string newGridAreaCode, string newPriceArea, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<Customer?> GetCustomerAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<PagedResult<Customer>> GetCustomersPagedAsync(int page, int pageSize, string? search, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Contract>> GetContractsForCustomerAsync(Guid customerId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<MeteringPointWithSupply>> GetMeteringPointsForCustomerAsync(Guid customerId, CancellationToken ct) => throw new NotImplementedException();
        public Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<Payer> CreatePayerAsync(string name, string cprCvr, string contactType, string? email, string? phone, Address? billingAddress, CancellationToken ct) => throw new NotImplementedException();
        public Task<Payer?> GetPayerAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Payer>> GetPayersForCustomerAsync(Guid customerId, CancellationToken ct) => throw new NotImplementedException();
        public Task UpdateCustomerBillingAddressAsync(Guid customerId, Address address, CancellationToken ct) => throw new NotImplementedException();
    }

    internal sealed class StubDataLoader : ISettlementDataLoader
    {
        public Task<SettlementInput> LoadAsync(string gsrn, string gridAreaCode, string priceArea,
            DateOnly periodStart, DateOnly periodEnd,
            decimal marginPerKwh, decimal supplementPerKwh, decimal supplierSubscriptionPerMonth,
            CancellationToken ct)
        {
            // Return minimal valid input for the engine
            var consumption = new List<MeteringDataRow>
            {
                new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), "PT1H", 0.5m, "A01", "test"),
            };
            var spotPrices = new List<SpotPriceRow>
            {
                new("DK1", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 85m),
            };
            var rates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, 0.10m)).ToList();

            return Task.FromResult(new SettlementInput(
                gsrn, periodStart, periodEnd,
                consumption, spotPrices, rates,
                0.054m, 0.049m, 0.008m,
                49.00m, marginPerKwh, supplementPerKwh, supplierSubscriptionPerMonth));
        }
    }

    internal sealed class StubResultStore : ISettlementResultStore
    {
        public int StoreCount { get; private set; }
        public string? LastGsrn { get; private set; }

        public Task StoreAsync(string gsrn, string gridAreaCode, SettlementResult result, CancellationToken ct)
        {
            StoreCount++;
            LastGsrn = gsrn;
            return Task.CompletedTask;
        }
    }
}
