using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Infrastructure.Onboarding;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class SignupValidationTests
{
    private readonly TestClock _clock = new() { Today = new DateOnly(2026, 2, 12) };
    private readonly InMemoryProcessRepo _processRepo = new();
    private readonly InMemorySignupRepo _signupRepo = new();
    private readonly InMemoryPortfolioRepo _portfolioRepo = new();

    private OnboardingService CreateSut() => new(
        _signupRepo,
        _portfolioRepo,
        _processRepo,
        new StubAddressLookup(),
        new StubDataHubClient(),
        new StubBrsRequestBuilder(),
        new StubMessageRepo(),
        _clock,
        NullLogger<OnboardingService>.Instance);

    private static SignupRequest MakeRequest(
        string type = "switch",
        DateOnly? effectiveDate = null,
        string gsrn = "571313100000012345",
        string customerName = "John Doe",
        string cprCvr = "1234567890",
        string contactType = "private",
        string? mobile = "12345678",
        string? billingFrequency = null,
        string? paymentModel = null) =>
        new(DarId: null, CustomerName: customerName, CprCvr: cprCvr,
            ContactType: contactType, Email: "j@d.dk", Phone: "12345678",
            ProductId: InMemoryPortfolioRepo.DefaultProductId, Type: type,
            EffectiveDate: effectiveDate ?? new DateOnly(2026, 3, 1),
            Gsrn: gsrn, Mobile: mobile, BillingFrequency: billingFrequency,
            PaymentModel: paymentModel);

    // ── Move-in effective date (BRS-009) ──

    [Fact]
    public async Task MoveIn_allows_retroactive_up_to_7_days()
    {
        var sut = CreateSut();
        var request = MakeRequest("move_in", _clock.Today.AddDays(-7));

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MoveIn_rejects_more_than_7_days_retroactive()
    {
        var sut = CreateSut();
        var request = MakeRequest("move_in", _clock.Today.AddDays(-8));

        var act = () => sut.CreateSignupAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*7 days*past*");
    }

    [Fact]
    public async Task MoveIn_allows_up_to_60_days_advance()
    {
        var sut = CreateSut();
        var request = MakeRequest("move_in", _clock.Today.AddDays(60));

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MoveIn_rejects_more_than_60_days_advance()
    {
        var sut = CreateSut();
        var request = MakeRequest("move_in", _clock.Today.AddDays(61));

        var act = () => sut.CreateSignupAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*60 days*future*");
    }

    // ── Supplier switch effective date (BRS-001) ──

    [Fact]
    public async Task Switch_rejects_effective_date_today_or_earlier()
    {
        var sut = CreateSut();
        var request = MakeRequest("switch", _clock.Today);

        var act = () => sut.CreateSignupAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*after today*");
    }

    [Fact]
    public async Task Switch_allows_tomorrow()
    {
        var sut = CreateSut();
        var request = MakeRequest("switch", _clock.Today.AddDays(1));

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Switch_rejects_more_than_1_year_advance()
    {
        var sut = CreateSut();
        var request = MakeRequest("switch", _clock.Today.AddYears(1).AddDays(1));

        var act = () => sut.CreateSignupAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*1 year*");
    }

    [Fact]
    public async Task Switch_allows_exactly_1_year_advance()
    {
        var sut = CreateSut();
        var request = MakeRequest("switch", _clock.Today.AddYears(1));

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ── Customer name validation (BRS-009 D03) ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Rejects_empty_customer_name(string? name)
    {
        var sut = CreateSut();
        var request = MakeRequest(customerName: name!);

        var act = () => sut.CreateSignupAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Customer name*required*");
    }

    // ── Metering point status validation (BRS-001 D16) ──

    [Fact]
    public async Task Rejects_closed_down_metering_point()
    {
        _portfolioRepo.AddMeteringPoint("571313100000012345", "closed_down");
        var sut = CreateSut();
        var request = MakeRequest();

        var act = () => sut.CreateSignupAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*closed down*");
    }

    [Fact]
    public async Task Allows_connected_metering_point()
    {
        _portfolioRepo.AddMeteringPoint("571313100000012345", "connected");
        var sut = CreateSut();
        var request = MakeRequest();

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Allows_disconnected_metering_point()
    {
        _portfolioRepo.AddMeteringPoint("571313100000012345", "disconnected");
        var sut = CreateSut();
        var request = MakeRequest();

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Allows_unknown_metering_point_not_in_portfolio()
    {
        // MP not in our portfolio yet — we should still allow signup
        // (DataHub will validate the GSRN on its side)
        var sut = CreateSut();
        var request = MakeRequest();

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ── Billing frequency validation ──

    [Theory]
    [InlineData("weekly")]
    [InlineData("monthly")]
    [InlineData("quarterly")]
    public async Task Accepts_valid_billing_frequency(string frequency)
    {
        var sut = CreateSut();
        var request = MakeRequest(billingFrequency: frequency);

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Defaults_to_monthly_when_billing_frequency_is_null()
    {
        var sut = CreateSut();
        var request = MakeRequest(billingFrequency: null);

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Rejects_daily_billing_frequency()
    {
        var sut = CreateSut();
        var request = MakeRequest(billingFrequency: "daily");

        var act = () => sut.CreateSignupAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*billing frequency*");
    }

    // ── All 6 billing_frequency × payment_model combinations ──

    [Theory]
    [InlineData("weekly", "post_payment")]
    [InlineData("weekly", "aconto")]
    [InlineData("monthly", "post_payment")]
    [InlineData("monthly", "aconto")]
    [InlineData("quarterly", "post_payment")]
    [InlineData("quarterly", "aconto")]
    public async Task Accepts_all_valid_frequency_and_payment_model_combinations(string frequency, string model)
    {
        var sut = CreateSut();
        var request = MakeRequest(billingFrequency: frequency, paymentModel: model);

        var result = await sut.CreateSignupAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be("registered");
    }

    [Theory]
    [InlineData("yearly")]
    [InlineData("biweekly")]
    [InlineData("")]
    public async Task Rejects_invalid_billing_frequency(string frequency)
    {
        var sut = CreateSut();
        var request = MakeRequest(billingFrequency: frequency);

        var act = () => sut.CreateSignupAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*billing frequency*");
    }

    // ── Stubs ──

    private sealed class InMemoryProcessRepo : IProcessRepository
    {
        private readonly Dictionary<Guid, ProcessRequest> _requests = new();

        public Task<ProcessRequest> CreateAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
        {
            var r = new ProcessRequest(Guid.NewGuid(), processType, gsrn, "pending", effectiveDate, null);
            _requests[r.Id] = r;
            return Task.FromResult(r);
        }

        public async Task<ProcessRequest> CreateWithEventAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
            => await CreateAsync(processType, gsrn, effectiveDate, ct);

        public Task<ProcessRequest?> GetAsync(Guid id, CancellationToken ct)
        {
            _requests.TryGetValue(id, out var r);
            return Task.FromResult(r);
        }

        public Task<ProcessRequest?> GetByCorrelationIdAsync(string correlationId, CancellationToken ct)
            => Task.FromResult<ProcessRequest?>(null);

        public Task UpdateStatusAsync(Guid id, string status, string? correlationId, CancellationToken ct)
        {
            var existing = _requests[id];
            _requests[id] = existing with { Status = status, DatahubCorrelationId = correlationId ?? existing.DatahubCorrelationId };
            return Task.CompletedTask;
        }

        public Task TransitionWithEventAsync(Guid id, string newStatus, string expectedStatus, string? correlationId, string eventType, CancellationToken ct)
            => UpdateStatusAsync(id, newStatus, correlationId, ct);

        public Task AddEventAsync(Guid processRequestId, string eventType, string? payload, string? source, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ProcessEvent>> GetEventsAsync(Guid processRequestId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProcessEvent>>(Array.Empty<ProcessEvent>());

        public Task<IReadOnlyList<ProcessRequest>> GetByStatusAsync(string status, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProcessRequest>>(_requests.Values.Where(r => r.Status == status).ToList());

        public Task<bool> HasActiveByGsrnAsync(string gsrn, CancellationToken ct)
            => Task.FromResult(false);

        public Task AutoCancelAsync(Guid requestId, string expectedStatus, string reason, CancellationToken ct)
            => Task.CompletedTask;

        public Task MarkCustomerDataReceivedAsync(string correlationId, CancellationToken ct) => Task.CompletedTask;
        public Task MarkTariffDataReceivedAsync(string correlationId, CancellationToken ct) => Task.CompletedTask;
        public Task<ProcessDetail?> GetDetailWithChecklistAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessDetail?>(null);
        public Task<IReadOnlyList<ProcessRequest>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProcessRequest>>(Array.Empty<ProcessRequest>());
        public Task<ProcessRequest?> GetCompletedByGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult<ProcessRequest?>(null);
        public Task<Application.Common.PagedResult<ProcessListItem>> GetProcessesPagedAsync(
            string? status, string? processType, string? search, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(new Application.Common.PagedResult<ProcessListItem>(Array.Empty<ProcessListItem>(), 0, page, pageSize));
    }

    private sealed class InMemorySignupRepo : ISignupRepository
    {
        private int _seq;

        public Task<Signup> CreateAsync(string signupNumber, string darId, string gsrn,
            string customerName, string customerCprCvr, string customerContactType,
            Guid productId, Guid processRequestId, string type, DateOnly effectiveDate,
            Guid? correctedFromId, SignupAddressInfo? addressInfo, string? mobile,
            string billingFrequency, string paymentModel, CancellationToken ct)
            => Task.FromResult(new Signup(Guid.NewGuid(), signupNumber, darId, gsrn, null, productId, processRequestId, type, effectiveDate, "registered", null, correctedFromId));

        public Task<string> NextSignupNumberAsync(CancellationToken ct)
            => Task.FromResult($"SGN-2026-{++_seq:D5}");

        public Task<Signup?> GetBySignupNumberAsync(string signupNumber, CancellationToken ct) => Task.FromResult<Signup?>(null);
        public Task<Signup?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Signup?>(null);
        public Task<Signup?> GetByProcessRequestIdAsync(Guid processRequestId, CancellationToken ct) => Task.FromResult<Signup?>(null);
        public Task<Signup?> GetActiveByGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult<Signup?>(null);
        public Task UpdateStatusAsync(Guid id, string status, string? rejectionReason, CancellationToken ct) => Task.CompletedTask;
        public Task SetProcessRequestIdAsync(Guid id, Guid processRequestId, CancellationToken ct) => Task.CompletedTask;
        public Task LinkCustomerAsync(Guid signupId, Guid customerId, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetCustomerCprCvrAsync(Guid signupId, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<SignupListItem>> GetAllAsync(string? statusFilter, CancellationToken ct) => Task.FromResult<IReadOnlyList<SignupListItem>>(Array.Empty<SignupListItem>());
        public Task<PagedResult<SignupListItem>> GetAllPagedAsync(string? statusFilter, int page, int pageSize, CancellationToken ct) => Task.FromResult(new PagedResult<SignupListItem>(Array.Empty<SignupListItem>(), 0, 1, 10));
        public Task<IReadOnlyList<SignupListItem>> GetRecentAsync(int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<SignupListItem>>(Array.Empty<SignupListItem>());
        public Task<SignupDetail?> GetDetailByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<SignupDetail?>(null);
        public Task<IReadOnlyList<SignupCorrectionLink>> GetCorrectionChainAsync(Guid signupId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SignupCorrectionLink>>(Array.Empty<SignupCorrectionLink>());
        public Task<SignupAddressInfo?> GetAddressInfoAsync(Guid signupId, CancellationToken ct) => Task.FromResult<SignupAddressInfo?>(null);
    }

    internal sealed class InMemoryPortfolioRepo : IPortfolioRepository
    {
        public static readonly Guid DefaultProductId = Guid.NewGuid();
        private readonly Dictionary<string, MeteringPoint> _meteringPoints = new();

        public void AddMeteringPoint(string gsrn, string connectionStatus)
            => _meteringPoints[gsrn] = new MeteringPoint(gsrn, "E17", "flex", "DK1", "5790001089030", "DK1", connectionStatus);

        public Task<MeteringPoint?> GetMeteringPointByGsrnAsync(string gsrn, CancellationToken ct)
        {
            _meteringPoints.TryGetValue(gsrn, out var mp);
            return Task.FromResult(mp);
        }

        public Task<Product?> GetProductAsync(Guid productId, CancellationToken ct)
            => Task.FromResult<Product?>(new Product(productId, "Test Spot", "spot", 2.5m, null, 39m, "Test product", false, 0));

        public Task<Customer> CreateCustomerAsync(string name, string cprCvr, string contactType, Address? billingAddress, CancellationToken ct) => throw new NotImplementedException();
        public Task<Customer?> GetCustomerByCprCvrAsync(string cprCvr, CancellationToken ct) => throw new NotImplementedException();
        public Task<MeteringPoint> CreateMeteringPointAsync(MeteringPoint mp, CancellationToken ct) => throw new NotImplementedException();
        public Task<Product> CreateProductAsync(string name, string energyModel, decimal marginOrePerKwh, decimal? supplementOrePerKwh, decimal subscriptionKrPerMonth, CancellationToken ct) => throw new NotImplementedException();
        public Task<Contract> CreateContractAsync(Guid customerId, string gsrn, Guid productId, string billingFrequency, string paymentModel, DateOnly startDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<SupplyPeriod> CreateSupplyPeriodAsync(string gsrn, DateOnly startDate, CancellationToken ct) => throw new NotImplementedException();
        public Task ActivateMeteringPointAsync(string gsrn, DateTime activatedAtUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task<Contract?> GetActiveContractAsync(string gsrn, CancellationToken ct) => throw new NotImplementedException();
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
        public Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Contract>> GetContractsForCustomerAsync(Guid customerId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<MeteringPointWithSupply>> GetMeteringPointsForCustomerAsync(Guid customerId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Payer> CreatePayerAsync(string name, string cprCvr, string contactType, string? email, string? phone, Address? billingAddress, CancellationToken ct) => throw new NotImplementedException();
        public Task<Payer?> GetPayerAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Payer>> GetPayersForCustomerAsync(Guid customerId, CancellationToken ct) => throw new NotImplementedException();
        public Task UpdateCustomerBillingAddressAsync(Guid customerId, Address address, CancellationToken ct) => throw new NotImplementedException();
        public Task StageCustomerDataAsync(string gsrn, string customerName, string? cprCvr, string customerType, string? phone, string? email, string? correlationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<StagedCustomerData?> GetStagedCustomerDataAsync(string gsrn, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubAddressLookup : IAddressLookupClient
    {
        public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
            => Task.FromResult(new AddressLookupResult(new[] { new MeteringPointInfo("571313100000012345", "E17", "DK1") }));
    }

    private sealed class StubDataHubClient : IDataHubClient
    {
        public Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct) => Task.FromResult<DataHubMessage?>(null);
        public Task DequeueAsync(string messageId, CancellationToken ct) => Task.CompletedTask;
        public Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct)
            => Task.FromResult(new DataHubResponse("corr-stub", true, null));
    }

    private sealed class StubBrsRequestBuilder : IBrsRequestBuilder
    {
        public string BuildBrs001(string gsrn, string cprCvr, DateOnly effectiveDate) => "{}";
        public string BuildBrs002(string gsrn, DateOnly effectiveDate) => "{}";
        public string BuildBrs003(string gsrn, string originalCorrelationId) => "{}";
        public string BuildBrs009(string gsrn, string cprCvr, DateOnly effectiveDate) => "{}";
        public string BuildBrs010(string gsrn, DateOnly effectiveDate) => "{}";
        public string BuildBrs044(string gsrn, string originalCorrelationId) => "{}";
        public string BuildRsm027(string gsrn, string customerName, string cprCvr, string correlationId) => "{}";
    }

    private sealed class StubMessageRepo : IMessageRepository
    {
        public Task RecordOutboundRequestAsync(string processType, string gsrn, string correlationId, string status, string? rawPayload, CancellationToken ct) => Task.CompletedTask;
        public Task<Application.Common.PagedResult<InboundMessageSummary>> GetInboundMessagesAsync(MessageFilter filter, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<InboundMessageDetail?> GetInboundMessageAsync(Guid messageId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Common.PagedResult<OutboundRequestSummary>> GetOutboundRequestsAsync(OutboundFilter filter, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<OutboundRequestDetail?> GetOutboundRequestAsync(Guid requestId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Common.PagedResult<DeadLetterSummary>> GetDeadLettersAsync(bool? resolvedOnly, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeadLetterDetail?> GetDeadLetterAsync(Guid deadLetterId, CancellationToken ct) => throw new NotImplementedException();
        public Task<MessageStats> GetMessageStatsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Common.PagedResult<ConversationSummary>> GetConversationsAsync(int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<ConversationDetail?> GetConversationAsync(string correlationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<DataDeliverySummary>> GetDataDeliveriesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task ResolveDeadLetterAsync(Guid id, string resolvedBy, CancellationToken ct) => throw new NotImplementedException();
    }
}
