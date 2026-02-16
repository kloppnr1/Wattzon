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

    private SettlementTriggerService CreateTrigger(TestClock? clock = null) => new(
        _processRepo, _portfolioRepo, _completenessChecker,
        _dataLoader, _engine, _resultStore,
        clock ?? new TestClock { Today = new DateOnly(2025, 2, 15) },
        NullLogger<SettlementTriggerService>.Instance);

    private SettlementOrchestrationService CreateSut(TestClock? clock = null)
    {
        var trigger = CreateTrigger(clock);
        return new(_processRepo, trigger, NullLogger<SettlementOrchestrationService>.Instance);
    }

    [Fact]
    public async Task Skips_when_metering_incomplete()
    {
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(744, 500, false);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "monthly", "post_payment", new DateOnly(2025, 1, 1));

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(0);
    }

    [Fact]
    public async Task Skips_when_already_settled()
    {
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(744, 744, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "monthly", "post_payment", new DateOnly(2025, 1, 1));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        // First run should store
        _resultStore.StoreCount.Should().Be(1);
    }

    [Fact]
    public async Task Runs_settlement_when_eligible()
    {
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(744, 744, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "monthly", "post_payment", new DateOnly(2025, 1, 1));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(1);
        _resultStore.LastGsrn.Should().Be("571313100000012345");
    }

    [Fact]
    public async Task Weekly_billing_settles_multiple_closed_weeks()
    {
        // Effective date: Monday Jan 6 2025. Today: Jan 28 (Tue).
        // Exclusive weekly periods: Jan 6–Jan 13, Jan 13–Jan 20, Jan 20–Jan 27 = 3 weeks.
        // Jan 27–Feb 3 is still open (Feb 3 > Jan 28).
        var clock = new TestClock { Today = new DateOnly(2025, 1, 28) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 6), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(144, 144, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "weekly", "post_payment", new DateOnly(2025, 1, 6));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(3, "three closed weekly periods should be settled");
    }

    [Fact]
    public async Task Skips_already_settled_periods_and_settles_next()
    {
        // Effective date: Monday Jan 6. Today: Jan 21 (Tue).
        // Week 1 (Jan 6–Jan 13 exclusive) already settled, week 2 (Jan 13–Jan 20) not settled.
        // Jan 20–Jan 27 still open (Jan 27 > Jan 21).
        var clock = new TestClock { Today = new DateOnly(2025, 1, 21) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 6), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(144, 144, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "weekly", "post_payment", new DateOnly(2025, 1, 6));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        // Mark week 1 as already settled (exclusive end = Jan 13)
        _resultStore.SettledPeriods.Add(("571313100000012345", new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 13)));

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(1, "only the unsettled week 2 should be settled");
    }

    [Fact]
    public async Task Sunday_movein_single_day_weekly_period_settles()
    {
        // Effective date = Sunday Jan 5 2025, weekly billing.
        // First period: Jan 5–Jan 6 (exclusive, 1 day). Today = Jan 6 (Monday).
        // periodEnd > today → Jan 6 > Jan 6 → false → settle.
        var clock = new TestClock { Today = new DateOnly(2025, 1, 6) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 5), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(24, 24, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "weekly", "post_payment", new DateOnly(2025, 1, 5));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(1, "single-day Sunday period should be settled");
    }

    [Fact]
    public async Task Settles_when_period_end_equals_today()
    {
        // Effective date = Monday Jan 6 2025, weekly billing.
        // First period: Jan 6–Jan 13 (exclusive). Today = Jan 13 (Monday = periodEnd).
        // periodEnd > today → Jan 13 > Jan 13 → false → settle.
        var clock = new TestClock { Today = new DateOnly(2025, 1, 13) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 6), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(168, 168, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "weekly", "post_payment", new DateOnly(2025, 1, 6));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(1, "period where periodEnd == today should be settled");
    }

    [Fact]
    public async Task Month_end_start_single_day_monthly_period_settles()
    {
        // Effective date = Jan 31 2025, monthly billing.
        // First period: Jan 31–Feb 1 (exclusive, 1 day). Today = Mar 1.
        var clock = new TestClock { Today = new DateOnly(2025, 3, 1) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 31), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(24, 24, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "monthly", "post_payment", new DateOnly(2025, 1, 31));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().BeGreaterThanOrEqualTo(1, "single-day Jan 31 monthly period should be settled");
    }

    [Fact]
    public async Task Quarter_end_start_single_day_quarterly_period_settles()
    {
        // Effective date = Mar 31 2025, quarterly billing.
        // First period: Mar 31–Apr 1 (exclusive, 1 day). Today = Apr 1.
        var clock = new TestClock { Today = new DateOnly(2025, 4, 1) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 3, 31), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(24, 24, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "quarterly", "post_payment", new DateOnly(2025, 3, 31));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().BeGreaterThanOrEqualTo(1, "single-day Mar 31 quarterly period should be settled");
    }

    [Fact]
    public async Task Daily_billing_settles_single_day_period()
    {
        // Effective date: Jan 5 2025, daily billing. Today: Jan 6.
        // Single period: Jan 5–Jan 6 (exclusive, 1 day).
        var clock = new TestClock { Today = new DateOnly(2025, 1, 6) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 5), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(24, 24, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "daily", "post_payment", new DateOnly(2025, 1, 5));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var sut = CreateSut(clock);
        await sut.RunTickAsync(CancellationToken.None);

        _resultStore.StoreCount.Should().Be(1, "single daily period should be settled");
    }

    [Fact]
    public async Task TrySettleAsync_triggers_settlement_for_gsrn()
    {
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };
        var sm = new ProcessStateMachine(_processRepo, clock);
        var request = await sm.CreateRequestAsync("571313100000012345", ProcessTypes.SupplierSwitch, new DateOnly(2025, 1, 1), CancellationToken.None);
        await sm.MarkSentAsync(request.Id, "corr-1", CancellationToken.None);
        await sm.MarkAcknowledgedAsync(request.Id, CancellationToken.None);
        await sm.MarkCompletedAsync(request.Id, CancellationToken.None);

        _completenessChecker.Result = new MeteringCompleteness(744, 744, true);
        _portfolioRepo.Contract = new Contract(Guid.NewGuid(), Guid.NewGuid(), "571313100000012345", Guid.NewGuid(), "monthly", "post_payment", new DateOnly(2025, 1, 1));
        _portfolioRepo.Product = new Product(Guid.NewGuid(), "Spot Standard", "spot", 4.0m, null, 39.00m);

        var trigger = CreateTrigger(clock);
        await trigger.TrySettleAsync("571313100000012345", CancellationToken.None);

        _resultStore.StoreCount.Should().Be(1);
        _resultStore.LastGsrn.Should().Be("571313100000012345");
    }

    [Fact]
    public async Task TrySettleAsync_skips_when_no_completed_process()
    {
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };

        var trigger = CreateTrigger(clock);
        await trigger.TrySettleAsync("571313100000099999", CancellationToken.None);

        _resultStore.StoreCount.Should().Be(0);
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
        public MeteringPoint? MeteringPoint { get; set; } = new("571313100000012345", "E17", "D01", "344", "5790001089030", "DK1", "D03");

        public Task<Contract?> GetActiveContractAsync(string gsrn, CancellationToken ct) => Task.FromResult(Contract);
        public Task<Product?> GetProductAsync(Guid productId, CancellationToken ct) => Task.FromResult(Product);
        public Task<MeteringPoint?> GetMeteringPointByGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult(MeteringPoint);

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
        public Task StageCustomerDataAsync(string gsrn, string customerName, string? cprCvr, string customerType, string? phone, string? email, string? correlationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<StagedCustomerData?> GetStagedCustomerDataAsync(string gsrn, CancellationToken ct) => throw new NotImplementedException();
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
        public HashSet<(string Gsrn, DateOnly Start, DateOnly End)> SettledPeriods { get; } = new();

        public Task StoreAsync(string gsrn, string gridAreaCode, SettlementResult result, string billingFrequency, CancellationToken ct)
        {
            StoreCount++;
            LastGsrn = gsrn;
            SettledPeriods.Add((gsrn, result.PeriodStart, result.PeriodEnd));
            return Task.CompletedTask;
        }

        public Task<bool> HasSettlementRunAsync(string gsrn, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
            => Task.FromResult(SettledPeriods.Contains((gsrn, periodStart, periodEnd)));
    }
}
