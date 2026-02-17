using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Domain.MasterData;
using DataHub.Settlement.Domain.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

#region Shared recording fakes

internal sealed class RecordingCimParser : ICimParser
{
    public Rsm014Aggregation? LastRsm014Result { get; set; }
    public ParsedMasterData? LastRsm022Result { get; set; }
    public Rsm004Result? LastRsm004Result { get; set; }
    public Rsm001ResponseResult? LastRsm001Result { get; set; }
    public Rsm001ResponseResult? LastRsm005Result { get; set; }
    public Rsm001ResponseResult? LastRsm024Result { get; set; }
    public Rsm028Result? LastRsm028Result { get; set; }
    public Rsm031Result? LastRsm031Result { get; set; }
    public Rsm034PriceSeriesResult? LastRsm034Result { get; set; }
    public IReadOnlyList<ParsedTimeSeries>? LastRsm012Result { get; set; }

    public Rsm014Aggregation ParseRsm014(string json) => LastRsm014Result!;
    public ParsedMasterData ParseRsm022(string json) => LastRsm022Result!;
    public Rsm004Result ParseRsm004(string json) => LastRsm004Result!;
    public Rsm001ResponseResult ParseRsm001Response(string json) => LastRsm001Result!;
    public Rsm001ResponseResult ParseRsm005Response(string json) => LastRsm005Result!;
    public Rsm001ResponseResult ParseRsm024Response(string json) => LastRsm024Result!;
    public Rsm028Result ParseRsm028(string json) => LastRsm028Result!;
    public Rsm031Result ParseRsm031(string json) => LastRsm031Result!;
    public Rsm034PriceSeriesResult ParseRsm034PriceSeries(string json) => LastRsm034Result!;
    public IReadOnlyList<ParsedTimeSeries> ParseRsm012(string json) => LastRsm012Result!;
}

internal sealed class RecordingPortfolioRepository : IPortfolioRepository
{
    public List<(string Code, string Gln, string Name, string PriceArea)> EnsuredGridAreas { get; } = new();
    public List<MeteringPoint> CreatedMeteringPoints { get; } = new();
    public List<(string Gsrn, DateTime ActivatedAt)> ActivatedPoints { get; } = new();
    public List<(string Gsrn, DateOnly StartDate)> CreatedSupplyPeriods { get; } = new();
    public List<(string Gsrn, DateOnly EndDate, string Reason)> EndedSupplyPeriods { get; } = new();
    public List<(string Gsrn, DateOnly EndDate)> EndedContracts { get; } = new();
    public List<(string Gsrn, string GridArea, string PriceArea)> UpdatedGridAreas { get; } = new();
    public List<(string Gsrn, string Name, string? CprCvr, string Type, string? Phone, string? Email, string? CorrelationId)> StagedCustomerData { get; } = new();

    public Task EnsureGridAreaAsync(string code, string gln, string name, string priceArea, CancellationToken ct)
    { EnsuredGridAreas.Add((code, gln, name, priceArea)); return Task.CompletedTask; }

    public Task<MeteringPoint> CreateMeteringPointAsync(MeteringPoint mp, CancellationToken ct)
    { CreatedMeteringPoints.Add(mp); return Task.FromResult(mp); }

    public Task ActivateMeteringPointAsync(string gsrn, DateTime activatedAt, CancellationToken ct)
    { ActivatedPoints.Add((gsrn, activatedAt)); return Task.CompletedTask; }

    public Task<SupplyPeriod> CreateSupplyPeriodAsync(string gsrn, DateOnly startDate, CancellationToken ct)
    { CreatedSupplyPeriods.Add((gsrn, startDate)); return Task.FromResult(new SupplyPeriod(Guid.NewGuid(), gsrn, startDate, null)); }

    public Task EndSupplyPeriodAsync(string gsrn, DateOnly endDate, string reason, CancellationToken ct)
    { EndedSupplyPeriods.Add((gsrn, endDate, reason)); return Task.CompletedTask; }

    public Task EndContractAsync(string gsrn, DateOnly endDate, CancellationToken ct)
    { EndedContracts.Add((gsrn, endDate)); return Task.CompletedTask; }

    public Task UpdateMeteringPointGridAreaAsync(string gsrn, string newGridAreaCode, string newPriceArea, CancellationToken ct)
    { UpdatedGridAreas.Add((gsrn, newGridAreaCode, newPriceArea)); return Task.CompletedTask; }

    public Task StageCustomerDataAsync(string gsrn, string name, string? cprCvr, string type, string? phone, string? email, string? correlationId, CancellationToken ct)
    { StagedCustomerData.Add((gsrn, name, cprCvr, type, phone, email, correlationId)); return Task.CompletedTask; }

    // Not-needed stubs
    public Task<Customer> CreateCustomerAsync(string name, string cprCvr, string contactType, Address? billingAddress, CancellationToken ct) => throw new NotImplementedException();
    public Task<Customer?> GetCustomerByCprCvrAsync(string cprCvr, CancellationToken ct) => Task.FromResult<Customer?>(null);
    public Task<Product> CreateProductAsync(string name, string energyModel, decimal marginOrePerKwh, decimal? supplementOrePerKwh, decimal subscriptionKrPerMonth, CancellationToken ct) => throw new NotImplementedException();
    public Task<Contract> CreateContractAsync(Guid customerId, string gsrn, Guid productId, string billingFrequency, string paymentModel, DateOnly startDate, CancellationToken ct) => throw new NotImplementedException();
    public Task<Contract?> GetActiveContractAsync(string gsrn, CancellationToken ct) => Task.FromResult<Contract?>(null);
    public Task<Product?> GetProductAsync(Guid productId, CancellationToken ct) => Task.FromResult<Product?>(null);
    public Task DeactivateMeteringPointAsync(string gsrn, DateTime deactivatedAtUtc, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<SupplyPeriod>> GetSupplyPeriodsAsync(string gsrn, CancellationToken ct) => Task.FromResult<IReadOnlyList<SupplyPeriod>>(Array.Empty<SupplyPeriod>());
    public Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Product>>(Array.Empty<Product>());
    public Task<Customer?> GetCustomerAsync(Guid id, CancellationToken ct) => Task.FromResult<Customer?>(null);
    public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Customer>>(Array.Empty<Customer>());
    public Task<Application.Portfolio.PagedResult<Customer>> GetCustomersPagedAsync(int page, int pageSize, string? search, CancellationToken ct) => Task.FromResult(new Application.Portfolio.PagedResult<Customer>(Array.Empty<Customer>(), 0, page, pageSize));
    public Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct) => Task.FromResult(new DashboardStats(0, 0, 0, 0));
    public Task<IReadOnlyList<Contract>> GetContractsForCustomerAsync(Guid customerId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Contract>>(Array.Empty<Contract>());
    public Task<IReadOnlyList<MeteringPointWithSupply>> GetMeteringPointsForCustomerAsync(Guid customerId, CancellationToken ct) => Task.FromResult<IReadOnlyList<MeteringPointWithSupply>>(Array.Empty<MeteringPointWithSupply>());
    public Task<Payer> CreatePayerAsync(string name, string cprCvr, string contactType, string? email, string? phone, Address? billingAddress, CancellationToken ct) => throw new NotImplementedException();
    public Task<Payer?> GetPayerAsync(Guid id, CancellationToken ct) => Task.FromResult<Payer?>(null);
    public Task<IReadOnlyList<Payer>> GetPayersForCustomerAsync(Guid customerId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Payer>>(Array.Empty<Payer>());
    public Task UpdateCustomerBillingAddressAsync(Guid customerId, Address address, CancellationToken ct) => Task.CompletedTask;
    public Task<MeteringPoint?> GetMeteringPointByGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult<MeteringPoint?>(null);
    public Task<StagedCustomerData?> GetStagedCustomerDataAsync(string gsrn, CancellationToken ct) => Task.FromResult<StagedCustomerData?>(null);
}

internal sealed class RecordingTariffRepository : ITariffRepository
{
    public List<(string GridArea, string TariffType, DateOnly ValidFrom, int RateCount)> SeededTariffs { get; } = new();
    public List<(string GridArea, string SubType, decimal Amount, DateOnly ValidFrom)> SeededSubscriptions { get; } = new();
    public List<(decimal Rate, DateOnly ValidFrom)> SeededElectricityTax { get; } = new();
    public List<(string Gsrn, int Count, string? CorrelationId)> StoredAttachments { get; } = new();

    public Task SeedGridTariffAsync(string gridAreaCode, string tariffType, DateOnly validFrom, IReadOnlyList<TariffRateRow> rates, CancellationToken ct)
    { SeededTariffs.Add((gridAreaCode, tariffType, validFrom, rates.Count)); return Task.CompletedTask; }

    public Task SeedSubscriptionAsync(string gridAreaCode, string subscriptionType, decimal amountPerMonth, DateOnly validFrom, CancellationToken ct)
    { SeededSubscriptions.Add((gridAreaCode, subscriptionType, amountPerMonth, validFrom)); return Task.CompletedTask; }

    public Task SeedElectricityTaxAsync(decimal ratePerKwh, DateOnly validFrom, CancellationToken ct)
    { SeededElectricityTax.Add((ratePerKwh, validFrom)); return Task.CompletedTask; }

    public Task StoreTariffAttachmentsAsync(string gsrn, IReadOnlyList<TariffAttachment> tariffs, string? correlationId, CancellationToken ct)
    { StoredAttachments.Add((gsrn, tariffs.Count, correlationId)); return Task.CompletedTask; }

    public Task<IReadOnlyList<TariffRateRow>> GetRatesAsync(string gridAreaCode, string tariffType, DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<TariffRateRow>>(Array.Empty<TariffRateRow>());
    public Task<decimal?> GetSubscriptionAsync(string gridAreaCode, string subscriptionType, DateOnly date, CancellationToken ct) => Task.FromResult<decimal?>(null);
    public Task<decimal?> GetElectricityTaxAsync(DateOnly date, CancellationToken ct) => Task.FromResult<decimal?>(null);
    public Task<IReadOnlyList<MeteringPointTariffAttachment>> GetAttachmentsForGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult<IReadOnlyList<MeteringPointTariffAttachment>>(Array.Empty<MeteringPointTariffAttachment>());
    public Task<TariffSubscriptionInfo?> GetSubscriptionInfoAsync(string gridAreaCode, string subscriptionType, DateOnly date, CancellationToken ct) => Task.FromResult<TariffSubscriptionInfo?>(null);
    public Task<TariffElectricityTaxInfo?> GetElectricityTaxInfoAsync(DateOnly date, CancellationToken ct) => Task.FromResult<TariffElectricityTaxInfo?>(null);
}

internal sealed class RecordingProcessRepository : IProcessRepository
{
    private readonly Dictionary<Guid, ProcessRequest> _requests = new();
    private readonly List<ProcessEvent> _events = new();
    public List<string> MarkedCustomerDataCorrelations { get; } = new();
    public List<string> MarkedTariffDataCorrelations { get; } = new();

    public ProcessRequest Seed(string processType, string gsrn, string status, DateOnly effectiveDate, string? correlationId = null)
    {
        var r = new ProcessRequest(Guid.NewGuid(), processType, gsrn, status, effectiveDate, correlationId);
        _requests[r.Id] = r;
        return r;
    }

    public Task<ProcessRequest> CreateAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
    {
        var r = new ProcessRequest(Guid.NewGuid(), processType, gsrn, "pending", effectiveDate, null);
        _requests[r.Id] = r;
        return Task.FromResult(r);
    }

    public async Task<ProcessRequest> CreateWithEventAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
    {
        var r = await CreateAsync(processType, gsrn, effectiveDate, ct);
        await AddEventAsync(r.Id, "created", null, "system", ct);
        return r;
    }

    public Task<ProcessRequest?> GetAsync(Guid id, CancellationToken ct)
    {
        _requests.TryGetValue(id, out var r);
        return Task.FromResult(r);
    }

    public Task<ProcessRequest?> GetByCorrelationIdAsync(string correlationId, CancellationToken ct)
    {
        var r = _requests.Values.FirstOrDefault(x => x.DatahubCorrelationId == correlationId);
        return Task.FromResult(r);
    }

    public Task UpdateStatusAsync(Guid id, string status, string? correlationId, CancellationToken ct)
    {
        var existing = _requests[id];
        _requests[id] = existing with { Status = status, DatahubCorrelationId = correlationId ?? existing.DatahubCorrelationId };
        return Task.CompletedTask;
    }

    public async Task TransitionWithEventAsync(Guid id, string newStatus, string expectedStatus, string? correlationId, string eventType, CancellationToken ct)
    {
        await UpdateStatusAsync(id, newStatus, correlationId, ct);
        await AddEventAsync(id, eventType, null, "system", ct);
    }

    public Task AddEventAsync(Guid processRequestId, string eventType, string? payload, string? source, CancellationToken ct)
    {
        _events.Add(new ProcessEvent(Guid.NewGuid(), processRequestId, DateTime.UtcNow, eventType, payload, source));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProcessEvent>> GetEventsAsync(Guid processRequestId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProcessEvent>>(_events.Where(e => e.ProcessRequestId == processRequestId).ToList());

    public Task<IReadOnlyList<ProcessRequest>> GetByStatusAsync(string status, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProcessRequest>>(_requests.Values.Where(r => r.Status == status).ToList());

    public Task<bool> HasActiveByGsrnAsync(string gsrn, CancellationToken ct)
        => Task.FromResult(_requests.Values.Any(r => r.Gsrn == gsrn && r.Status is not ("completed" or "cancelled" or "rejected" or "final_settled")));

    public Task AutoCancelAsync(Guid requestId, string expectedStatus, string reason, CancellationToken ct)
    {
        if (!_requests.TryGetValue(requestId, out var req))
            throw new InvalidOperationException($"Process request {requestId} not found");
        if (req.Status != expectedStatus)
            throw new InvalidOperationException($"Cannot auto-cancel: expected '{expectedStatus}', got '{req.Status}'");
        _requests[requestId] = req with { Status = "cancelled" };
        return Task.CompletedTask;
    }

    public Task MarkCustomerDataReceivedAsync(string correlationId, CancellationToken ct)
    { MarkedCustomerDataCorrelations.Add(correlationId); return Task.CompletedTask; }

    public Task MarkTariffDataReceivedAsync(string correlationId, CancellationToken ct)
    { MarkedTariffDataCorrelations.Add(correlationId); return Task.CompletedTask; }

    public Task<ProcessDetail?> GetDetailWithChecklistAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessDetail?>(null);
    public Task<IReadOnlyList<ProcessRequest>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProcessRequest>>(Array.Empty<ProcessRequest>());
    public Task<ProcessRequest?> GetCompletedByGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult<ProcessRequest?>(null);
    public Task<Application.Common.PagedResult<ProcessListItem>> GetProcessesPagedAsync(string? status, string? processType, string? search, int page, int pageSize, CancellationToken ct)
        => Task.FromResult(new Application.Common.PagedResult<ProcessListItem>(Array.Empty<ProcessListItem>(), 0, page, pageSize));
}

internal sealed class RecordingSignupRepository : ISignupRepository
{
    private readonly Dictionary<string, Signup> _byGsrn = new();

    public void Seed(Signup signup) => _byGsrn[signup.Gsrn] = signup;

    public Task<Signup?> GetActiveByGsrnAsync(string gsrn, CancellationToken ct)
        => Task.FromResult(_byGsrn.GetValueOrDefault(gsrn));

    // Stubs
    public Task<Signup> CreateAsync(string signupNumber, string darId, string gsrn, string customerName, string customerCprCvr, string customerContactType, Guid productId, Guid processRequestId, string type, DateOnly effectiveDate, Guid? correctedFromId, SignupAddressInfo? addressInfo, string? mobile, string billingFrequency, string paymentModel, CancellationToken ct)
        => Task.FromResult(new Signup(Guid.NewGuid(), signupNumber, darId, gsrn, null, productId, processRequestId, type, effectiveDate, "registered", null, correctedFromId));
    public Task<string> NextSignupNumberAsync(CancellationToken ct) => Task.FromResult("SGN-TEST-00001");
    public Task<Signup?> GetBySignupNumberAsync(string signupNumber, CancellationToken ct) => Task.FromResult<Signup?>(null);
    public Task<Signup?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Signup?>(null);
    public Task<Signup?> GetByProcessRequestIdAsync(Guid processRequestId, CancellationToken ct) => Task.FromResult<Signup?>(null);
    public Task UpdateStatusAsync(Guid id, string status, string? rejectionReason, CancellationToken ct) => Task.CompletedTask;
    public Task SetProcessRequestIdAsync(Guid id, Guid processRequestId, CancellationToken ct) => Task.CompletedTask;
    public Task LinkCustomerAsync(Guid signupId, Guid customerId, CancellationToken ct) => Task.CompletedTask;
    public Task<string?> GetCustomerCprCvrAsync(Guid signupId, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<SignupListItem>> GetAllAsync(string? statusFilter, CancellationToken ct) => Task.FromResult<IReadOnlyList<SignupListItem>>(Array.Empty<SignupListItem>());
    public Task<Application.Portfolio.PagedResult<SignupListItem>> GetAllPagedAsync(string? statusFilter, int page, int pageSize, CancellationToken ct) => Task.FromResult(new Application.Portfolio.PagedResult<SignupListItem>(Array.Empty<SignupListItem>(), 0, page, pageSize));
    public Task<IReadOnlyList<SignupListItem>> GetRecentAsync(int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<SignupListItem>>(Array.Empty<SignupListItem>());
    public Task<SignupDetail?> GetDetailByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<SignupDetail?>(null);
    public Task<IReadOnlyList<SignupCorrectionLink>> GetCorrectionChainAsync(Guid signupId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SignupCorrectionLink>>(Array.Empty<SignupCorrectionLink>());
    public Task<SignupAddressInfo?> GetAddressInfoAsync(Guid signupId, CancellationToken ct) => Task.FromResult<SignupAddressInfo?>(null);
}

internal sealed class RecordingMeteringDataRepository : IMeteringDataRepository
{
    public List<(string Gsrn, IReadOnlyList<MeteringDataRow> Rows)> StoredSeries { get; } = new();
    public int ChangedCountToReturn { get; set; } = 0;

    public Task StoreTimeSeriesAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct)
    { StoredSeries.Add((meteringPointId, rows)); return Task.CompletedTask; }

    public Task<int> StoreTimeSeriesWithHistoryAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct)
    { StoredSeries.Add((meteringPointId, rows)); return Task.FromResult(ChangedCountToReturn); }

    public Task<IReadOnlyList<MeteringDataRow>> GetConsumptionAsync(string meteringPointId, DateTime from, DateTime to, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MeteringDataRow>>(Array.Empty<MeteringDataRow>());

    public Task<IReadOnlyList<MeteringDataChange>> GetChangesAsync(string meteringPointId, DateTime from, DateTime to, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MeteringDataChange>>(Array.Empty<MeteringDataChange>());
}

internal sealed class RecordingOnboardingService : IOnboardingService
{
    public List<(Guid ProcessId, string Status, string? Reason)> SyncCalls { get; } = new();

    public Task SyncFromProcessAsync(Guid processRequestId, string processStatus, string? reason, CancellationToken ct)
    { SyncCalls.Add((processRequestId, processStatus, reason)); return Task.CompletedTask; }

    public Task<AddressLookupResponse> LookupAddressAsync(string darId, CancellationToken ct) => Task.FromResult(new AddressLookupResponse(Array.Empty<MeteringPointResponse>()));
    public Task<AddressLookupResponse> ValidateGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult(new AddressLookupResponse(Array.Empty<MeteringPointResponse>()));
    public Task<SignupResponse> CreateSignupAsync(SignupRequest request, CancellationToken ct) => throw new NotImplementedException();
    public Task<SignupStatusResponse?> GetStatusAsync(string signupNumber, CancellationToken ct) => Task.FromResult<SignupStatusResponse?>(null);
    public Task CancelAsync(string signupNumber, CancellationToken ct) => Task.CompletedTask;
}

#endregion

#region AggregationsMessageHandler Tests

public class AggregationsMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_parses_RSM014_and_completes()
    {
        var parser = new RecordingCimParser
        {
            LastRsm014Result = new Rsm014Aggregation("344", new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1), 1234.5m,
                new List<AggregationPoint> { new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 50m) })
        };
        var sut = new AggregationsMessageHandler(parser, NullLogger<AggregationsMessageHandler>.Instance);

        var msg = new DataHubMessage("msg-1", "RSM-014", null, "{}");
        await sut.HandleAsync(msg, CancellationToken.None);

        sut.Queue.Should().Be(QueueName.Aggregations);
    }
}

#endregion

#region ChargesMessageHandler Tests

public class ChargesMessageHandlerTests
{
    private readonly RecordingCimParser _parser = new();
    private readonly RecordingPortfolioRepository _portfolioRepo = new();
    private readonly RecordingTariffRepository _tariffRepo = new();
    private ChargesMessageHandler CreateSut() =>
        new(_parser, _portfolioRepo, _tariffRepo, NullLogger<ChargesMessageHandler>.Instance);

    [Fact]
    public async Task HandleAsync_RSM034_seeds_tariff_and_subscription()
    {
        var rates = new List<TariffRateRow> { new(0, 0.25m), new(1, 0.30m) };
        _parser.LastRsm034Result = new Rsm034PriceSeriesResult(
            "344", "GLN-OWNER", new DateOnly(2025, 3, 1), "grid_tariff", rates,
            "grid_subscription", 50m, null);

        var sut = CreateSut();
        var msg = new DataHubMessage("msg-1", "RSM-034", null, "{}");
        await sut.HandleAsync(msg, CancellationToken.None);

        sut.Queue.Should().Be(QueueName.Charges);
        _portfolioRepo.EnsuredGridAreas.Should().ContainSingle()
            .Which.Code.Should().Be("344");
        _tariffRepo.SeededTariffs.Should().ContainSingle()
            .Which.Should().Be(("344", "grid_tariff", new DateOnly(2025, 3, 1), 2));
        _tariffRepo.SeededSubscriptions.Should().ContainSingle()
            .Which.Should().Be(("344", "grid_subscription", 50m, new DateOnly(2025, 3, 1)));
        _tariffRepo.SeededElectricityTax.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_RSM034_with_electricity_tax_seeds_tax()
    {
        _parser.LastRsm034Result = new Rsm034PriceSeriesResult(
            "740", "GLN-OWNER", new DateOnly(2025, 1, 1), "grid_tariff",
            new List<TariffRateRow> { new(0, 0.10m) },
            "grid_subscription", 30m, 0.008m);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-2", "RSM-034", null, "{}"), CancellationToken.None);

        _tariffRepo.SeededElectricityTax.Should().ContainSingle()
            .Which.Should().Be((0.008m, new DateOnly(2025, 1, 1)));
    }

    [Theory]
    [InlineData("RSM-034")]
    [InlineData("rsm-034")]
    [InlineData("RSM034")]
    public async Task HandleAsync_recognizes_RSM034_variants(string messageType)
    {
        _parser.LastRsm034Result = new Rsm034PriceSeriesResult(
            "344", "GLN", new DateOnly(2025, 1, 1), "t", new List<TariffRateRow>(), "s", 10m, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-3", messageType, null, "{}"), CancellationToken.None);

        _tariffRepo.SeededTariffs.Should().HaveCount(1);
    }

    [Fact]
    public async Task HandleAsync_non_RSM034_does_not_seed()
    {
        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-4", "RSM-999", null, "{}"), CancellationToken.None);

        _tariffRepo.SeededTariffs.Should().BeEmpty();
        _tariffRepo.SeededSubscriptions.Should().BeEmpty();
    }
}

#endregion

#region TimeseriesMessageHandler Tests

public class TimeseriesMessageHandlerTests
{
    private readonly RecordingCimParser _parser = new();
    private readonly RecordingMeteringDataRepository _meteringRepo = new();

    private TimeseriesMessageHandler CreateSut(ICorrectionService? correctionService = null) =>
        new(_parser, _meteringRepo, NullLogger<TimeseriesMessageHandler>.Instance,
            settlementTrigger: null, correctionService: correctionService);

    [Fact]
    public async Task HandleAsync_stores_valid_points_and_skips_negatives()
    {
        var regTime = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
        _parser.LastRsm012Result = new List<ParsedTimeSeries>
        {
            new("tx-1", "571313100000012345", "E17", "PT1H",
                regTime, regTime.AddHours(3), regTime,
                new List<TimeSeriesPoint>
                {
                    new(1, regTime, 1.5m, "E01"),
                    new(2, regTime.AddHours(1), -0.5m, "E01"),  // negative, should be skipped
                    new(3, regTime.AddHours(2), 2.0m, "E01"),
                })
        };

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-1", "RSM-012", null, "{}"), CancellationToken.None);

        sut.Queue.Should().Be(QueueName.Timeseries);
        _meteringRepo.StoredSeries.Should().ContainSingle();
        _meteringRepo.StoredSeries[0].Gsrn.Should().Be("571313100000012345");
        _meteringRepo.StoredSeries[0].Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task HandleAsync_empty_series_is_not_stored()
    {
        _parser.LastRsm012Result = new List<ParsedTimeSeries>
        {
            new("tx-2", "571313100000099999", "E17", "PT1H",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow,
                new List<TimeSeriesPoint>
                {
                    new(1, DateTimeOffset.UtcNow, -1m, "E01") // all negative
                })
        };

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-2", "RSM-012", null, "{}"), CancellationToken.None);

        _meteringRepo.StoredSeries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_multiple_series_stored_separately()
    {
        var regTime = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
        _parser.LastRsm012Result = new List<ParsedTimeSeries>
        {
            new("tx-a", "GSRN-A", "E17", "PT1H", regTime, regTime.AddHours(1), regTime,
                new List<TimeSeriesPoint> { new(1, regTime, 1.0m, "E01") }),
            new("tx-b", "GSRN-B", "E17", "PT1H", regTime, regTime.AddHours(1), regTime,
                new List<TimeSeriesPoint> { new(1, regTime, 2.0m, "E01") }),
        };

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-3", "RSM-012", null, "{}"), CancellationToken.None);

        _meteringRepo.StoredSeries.Should().HaveCount(2);
    }
}

#endregion

#region MasterDataMessageHandler Tests — RSM-001, RSM-005, RSM-024, RSM-028, RSM-031, RSM-004

public class MasterDataMessageHandlerTests
{
    private readonly RecordingCimParser _parser = new();
    private readonly RecordingPortfolioRepository _portfolioRepo = new();
    private readonly RecordingProcessRepository _processRepo = new();
    private readonly RecordingSignupRepository _signupRepo = new();
    private readonly RecordingOnboardingService _onboardingService = new();
    private readonly RecordingTariffRepository _tariffRepo = new();
    private readonly TestClock _clock = new() { Today = new DateOnly(2025, 6, 1) };

    // EffectuationService needs a real DB connection which we can't provide in unit tests,
    // so we test the code paths that DON'T go through EffectuationService (no signup, cancelled process, etc.)
    // and the non-RSM-022 handlers fully.
    private MasterDataMessageHandler CreateSut(EffectuationService? effectuationService = null) =>
        new(_parser, _portfolioRepo, _processRepo, _signupRepo, _onboardingService, _tariffRepo, _clock,
            effectuationService!, NullLogger<MasterDataMessageHandler>.Instance);

    #region RSM-001 (Supplier Switch Response)

    [Fact]
    public async Task RSM001_accepted_marks_process_acknowledged()
    {
        var process = _processRepo.Seed(ProcessTypes.SupplierSwitch, "GSRN-1", "sent_to_datahub", new DateOnly(2025, 3, 1), "corr-001");
        _parser.LastRsm001Result = new Rsm001ResponseResult("corr-001", true, null, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-1", "RSM-001", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("effectuation_pending");
        _onboardingService.SyncCalls.Should().ContainSingle()
            .Which.Status.Should().Be("effectuation_pending");
    }

    [Fact]
    public async Task RSM001_rejected_marks_process_rejected()
    {
        var process = _processRepo.Seed(ProcessTypes.SupplierSwitch, "GSRN-1", "sent_to_datahub", new DateOnly(2025, 3, 1), "corr-002");
        _parser.LastRsm001Result = new Rsm001ResponseResult("corr-002", false, "Duplicate request", "E0H");

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-2", "RSM-001", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("rejected");
        _onboardingService.SyncCalls.Should().ContainSingle()
            .Which.Should().Be((process.Id, "rejected", "Duplicate request"));
    }

    [Fact]
    public async Task RSM001_no_process_found_does_not_throw()
    {
        _parser.LastRsm001Result = new Rsm001ResponseResult("corr-unknown", true, null, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-3", "RSM-001", null, "{}"), CancellationToken.None);

        _onboardingService.SyncCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RSM001_cancellation_pending_accepted_marks_cancelled()
    {
        var process = _processRepo.Seed(ProcessTypes.SupplierSwitch, "GSRN-1", "cancellation_pending", new DateOnly(2025, 3, 1), "corr-003");
        _parser.LastRsm001Result = new Rsm001ResponseResult("corr-003", true, null, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-4", "RSM-001", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("cancelled");
        _onboardingService.SyncCalls.Should().ContainSingle()
            .Which.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task RSM001_cancellation_pending_rejected_reverts_to_effectuation_pending()
    {
        var process = _processRepo.Seed(ProcessTypes.SupplierSwitch, "GSRN-1", "cancellation_pending", new DateOnly(2025, 3, 1), "corr-004");
        _parser.LastRsm001Result = new Rsm001ResponseResult("corr-004", false, "Too late", null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-5", "RSM-001", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("effectuation_pending");
        _onboardingService.SyncCalls.Should().ContainSingle()
            .Which.Status.Should().Be("effectuation_pending");
    }

    #endregion

    #region RSM-005 (End of Supply Response)

    [Fact]
    public async Task RSM005_accepted_marks_completed_and_ends_supply()
    {
        var process = _processRepo.Seed(ProcessTypes.EndOfSupply, "GSRN-2", "sent_to_datahub",
            new DateOnly(2025, 4, 1), "corr-005");
        _parser.LastRsm005Result = new Rsm001ResponseResult("corr-005", true, null, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-6", "RSM-005", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("completed");

        _portfolioRepo.EndedSupplyPeriods.Should().ContainSingle()
            .Which.Should().Be(("GSRN-2", new DateOnly(2025, 4, 1), ProcessTypes.EndOfSupply));
        _portfolioRepo.EndedContracts.Should().ContainSingle()
            .Which.Should().Be(("GSRN-2", new DateOnly(2025, 4, 1)));

        _onboardingService.SyncCalls.Should().ContainSingle()
            .Which.Status.Should().Be("completed");
    }

    [Fact]
    public async Task RSM005_rejected_marks_rejected()
    {
        var process = _processRepo.Seed(ProcessTypes.EndOfSupply, "GSRN-2", "sent_to_datahub",
            new DateOnly(2025, 4, 1), "corr-006");
        _parser.LastRsm005Result = new Rsm001ResponseResult("corr-006", false, "Not valid", "E99");

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-7", "RSM-005", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("rejected");
        _portfolioRepo.EndedSupplyPeriods.Should().BeEmpty();
    }

    [Fact]
    public async Task RSM005_no_process_found_does_not_throw()
    {
        _parser.LastRsm005Result = new Rsm001ResponseResult("corr-unknown", true, null, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-8", "RSM-005", null, "{}"), CancellationToken.None);

        _onboardingService.SyncCalls.Should().BeEmpty();
    }

    #endregion

    #region RSM-024 (Cancellation Response)

    [Fact]
    public async Task RSM024_accepted_marks_cancelled()
    {
        var process = _processRepo.Seed(ProcessTypes.CancelSwitch, "GSRN-3", "cancellation_pending",
            new DateOnly(2025, 5, 1), "corr-007");
        _parser.LastRsm024Result = new Rsm001ResponseResult("corr-007", true, null, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-9", "RSM-024", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task RSM024_rejected_reverts_to_effectuation_pending()
    {
        var process = _processRepo.Seed(ProcessTypes.CancelSwitch, "GSRN-3", "cancellation_pending",
            new DateOnly(2025, 5, 1), "corr-008");
        _parser.LastRsm024Result = new Rsm001ResponseResult("corr-008", false, "Cannot cancel", null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-10", "RSM-024", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("effectuation_pending");
    }

    #endregion

    #region RSM-028 (Customer Data)

    [Fact]
    public async Task RSM028_stages_customer_data_and_marks_received()
    {
        _parser.LastRsm028Result = new Rsm028Result("msg-11", "GSRN-4", "John Doe", "1234567890", "person", "+4512345678", "john@example.com");

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-11", "RSM-028", "corr-009", "{}"), CancellationToken.None);

        _portfolioRepo.StagedCustomerData.Should().ContainSingle()
            .Which.Should().Be(("GSRN-4", "John Doe", "1234567890", "person", "+4512345678", "john@example.com", "corr-009"));
        _processRepo.MarkedCustomerDataCorrelations.Should().ContainSingle()
            .Which.Should().Be("corr-009");
    }

    [Fact]
    public async Task RSM028_without_correlation_does_not_mark_process()
    {
        _parser.LastRsm028Result = new Rsm028Result("msg-12", "GSRN-5", "Jane", "9876543210", "company", null, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-12", "RSM-028", null, "{}"), CancellationToken.None);

        _portfolioRepo.StagedCustomerData.Should().HaveCount(1);
        _processRepo.MarkedCustomerDataCorrelations.Should().BeEmpty();
    }

    #endregion

    #region RSM-031 (Tariff Data)

    [Fact]
    public async Task RSM031_stores_attachments_and_marks_received()
    {
        var tariffs = new List<TariffAttachment>
        {
            new("TAR-1", "grid_tariff", new DateOnly(2025, 1, 1), null),
            new("TAR-2", "system_tariff", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
        };
        _parser.LastRsm031Result = new Rsm031Result("msg-13", "GSRN-6", tariffs);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-13", "RSM-031", "corr-010", "{}"), CancellationToken.None);

        _tariffRepo.StoredAttachments.Should().ContainSingle()
            .Which.Should().Be(("GSRN-6", 2, "corr-010"));
        _processRepo.MarkedTariffDataCorrelations.Should().ContainSingle()
            .Which.Should().Be("corr-010");
    }

    #endregion

    #region RSM-004 (Change Notifications)

    [Fact]
    public async Task RSM004_auto_cancel_marks_process_cancelled()
    {
        var processId = Guid.NewGuid();
        var process = _processRepo.Seed(ProcessTypes.SupplierSwitch, "GSRN-7", "effectuation_pending",
            new DateOnly(2025, 3, 1), "corr-011");
        var signup = new Signup(Guid.NewGuid(), "SGN-001", "dar-1", "GSRN-7", null, Guid.NewGuid(),
            process.Id, ProcessTypes.SupplierSwitch, new DateOnly(2025, 3, 1), "registered", null, null);
        _signupRepo.Seed(signup);

        _parser.LastRsm004Result = new Rsm004Result("GSRN-7", null, null, null,
            new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero), Rsm004ReasonCodes.AutoCancel);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-14", "RSM-004", null, "{}"), CancellationToken.None);

        var updated = await _processRepo.GetAsync(process.Id, CancellationToken.None);
        updated!.Status.Should().Be("cancelled");
        _onboardingService.SyncCalls.Should().ContainSingle()
            .Which.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task RSM004_forced_transfer_ends_supply_and_contract()
    {
        _parser.LastRsm004Result = new Rsm004Result("GSRN-8", null, null, null,
            new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), Rsm004ReasonCodes.ForcedTransfer);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-15", "RSM-004", null, "{}"), CancellationToken.None);

        _portfolioRepo.EndedSupplyPeriods.Should().ContainSingle()
            .Which.Should().Be(("GSRN-8", new DateOnly(2025, 6, 1), "forced_transfer"));
        _portfolioRepo.EndedContracts.Should().ContainSingle()
            .Which.Should().Be(("GSRN-8", new DateOnly(2025, 6, 1)));
    }

    [Fact]
    public async Task RSM004_stop_of_supply_by_other_supplier_ends_supply()
    {
        _parser.LastRsm004Result = new Rsm004Result("GSRN-9", null, null, null,
            new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero), Rsm004ReasonCodes.StopOfSupplyByOtherSupplier);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-16", "RSM-004", null, "{}"), CancellationToken.None);

        _portfolioRepo.EndedSupplyPeriods.Should().ContainSingle()
            .Which.Reason.Should().Be("other_supplier_takeover");
    }

    [Fact]
    public async Task RSM004_stop_of_supply_ends_supply()
    {
        _parser.LastRsm004Result = new Rsm004Result("GSRN-10", null, null, null,
            new DateTimeOffset(2025, 8, 1, 0, 0, 0, TimeSpan.Zero), Rsm004ReasonCodes.StopOfSupply);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-17", "RSM-004", null, "{}"), CancellationToken.None);

        _portfolioRepo.EndedSupplyPeriods.Should().ContainSingle()
            .Which.Reason.Should().Be("stop_of_supply");
    }

    [Fact]
    public async Task RSM004_grid_area_change_updates_metering_point()
    {
        _parser.LastRsm004Result = new Rsm004Result("GSRN-11", "740", null, null,
            new DateTimeOffset(2025, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-18", "RSM-004", null, "{}"), CancellationToken.None);

        _portfolioRepo.UpdatedGridAreas.Should().ContainSingle()
            .Which.Should().Be(("GSRN-11", "740", "DK2"));
    }

    [Fact]
    public async Task RSM004_correction_accepted_does_not_modify_portfolio()
    {
        _parser.LastRsm004Result = new Rsm004Result("GSRN-12", null, null, null,
            DateTimeOffset.UtcNow, Rsm004ReasonCodes.CorrectionAccepted);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-19", "RSM-004", null, "{}"), CancellationToken.None);

        _portfolioRepo.EndedSupplyPeriods.Should().BeEmpty();
        _portfolioRepo.EndedContracts.Should().BeEmpty();
        _portfolioRepo.UpdatedGridAreas.Should().BeEmpty();
    }

    #endregion

    #region RSM-022 (MasterData / Activation) — paths NOT requiring EffectuationService

    [Fact]
    public async Task RSM022_no_signup_creates_supply_period_only()
    {
        _parser.LastRsm022Result = new ParsedMasterData(
            "msg-20", "GSRN-13", "E17", "D01", "344", "GLN-OP", "DK1",
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-20", "RSM-022", null, "{}"), CancellationToken.None);

        _portfolioRepo.EnsuredGridAreas.Should().ContainSingle().Which.Code.Should().Be("344");
        _portfolioRepo.CreatedMeteringPoints.Should().ContainSingle().Which.Gsrn.Should().Be("GSRN-13");
        _portfolioRepo.ActivatedPoints.Should().ContainSingle().Which.Gsrn.Should().Be("GSRN-13");
        _portfolioRepo.CreatedSupplyPeriods.Should().ContainSingle()
            .Which.Should().Be(("GSRN-13", new DateOnly(2025, 2, 1)));
    }

    [Fact]
    public async Task RSM022_cancelled_process_is_skipped()
    {
        var process = _processRepo.Seed(ProcessTypes.SupplierSwitch, "GSRN-14", "cancelled",
            new DateOnly(2025, 3, 1), "corr-012");
        var signup = new Signup(Guid.NewGuid(), "SGN-002", "dar-2", "GSRN-14", null, Guid.NewGuid(),
            process.Id, ProcessTypes.SupplierSwitch, new DateOnly(2025, 3, 1), "registered", null, null);
        _signupRepo.Seed(signup);

        _parser.LastRsm022Result = new ParsedMasterData(
            "msg-21", "GSRN-14", "E17", "D01", "344", "GLN-OP", "DK1",
            new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-21", "RSM-022", null, "{}"), CancellationToken.None);

        // Should still ensure grid area, create metering point, activate — but NOT go through effectuation
        _portfolioRepo.EnsuredGridAreas.Should().HaveCount(1);
        _portfolioRepo.ActivatedPoints.Should().HaveCount(1);
        _portfolioRepo.CreatedSupplyPeriods.Should().BeEmpty(); // no supply period for cancelled
    }

    [Fact]
    public async Task RSM022_signup_without_process_request_logs_warning()
    {
        var signup = new Signup(Guid.NewGuid(), "SGN-003", "dar-3", "GSRN-15", null, Guid.NewGuid(),
            null, ProcessTypes.SupplierSwitch, new DateOnly(2025, 3, 1), "registered", null, null);
        _signupRepo.Seed(signup);

        _parser.LastRsm022Result = new ParsedMasterData(
            "msg-22", "GSRN-15", "E17", "D01", "344", "GLN-OP", "DK1",
            new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var sut = CreateSut();
        // Should not throw — just logs warning
        await sut.HandleAsync(new DataHubMessage("msg-22", "RSM-022", null, "{}"), CancellationToken.None);

        _portfolioRepo.EnsuredGridAreas.Should().HaveCount(1);
        _portfolioRepo.ActivatedPoints.Should().HaveCount(1);
    }

    #endregion

    #region Unknown message type

    [Fact]
    public async Task Unknown_message_type_does_not_throw()
    {
        var sut = CreateSut();
        // No parser setup needed — won't be called for unknown types
        await sut.HandleAsync(new DataHubMessage("msg-99", "RSM-999", null, "{}"), CancellationToken.None);

        // Just verify no exceptions
        sut.Queue.Should().Be(QueueName.MasterData);
    }

    #endregion

    #region Message type normalization routing

    [Theory]
    [InlineData("RSM-028")]
    [InlineData("rsm-028")]
    [InlineData("RSM028")]
    public async Task RSM028_routed_regardless_of_format(string messageType)
    {
        _parser.LastRsm028Result = new Rsm028Result("msg-norm", "GSRN-NORM", "Test", "123", "person", null, null);

        var sut = CreateSut();
        await sut.HandleAsync(new DataHubMessage("msg-norm", messageType, "corr-norm", "{}"), CancellationToken.None);

        _portfolioRepo.StagedCustomerData.Should().HaveCount(1);
    }

    #endregion
}

#endregion
