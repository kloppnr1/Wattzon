using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Domain.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

/// <summary>
/// Unit tests that verify the process timeline events are created in the correct
/// order for all known signup lifecycle scenarios.
/// Uses in-memory repositories and fakes — no database, no simulator, no delays.
/// </summary>
public class ProcessTimelineTests
{
    private readonly ProcessStateMachineTests.InMemoryProcessRepository _processRepo = new();
    private readonly FakeDataHubClient _dataHubClient = new();
    private readonly TestClock _clock = new();

    private ProcessStateMachine CreateStateMachine() => new(_processRepo, _clock);

    // ══════════════════════════════════════════════════════════════
    //  SUNSHINE PATH: created → sent → acknowledged → awaiting_effectuation → completed
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Sunshine_path_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-sunshine-001", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);
        await sm.MarkCompletedAsync(request.Id, ct);

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation", "completed");
        events.Should().HaveCount(5);

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("completed");
    }

    // ══════════════════════════════════════════════════════════════
    //  REJECTION PATH: created → sent → rejected + rejection_reason
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Rejection_path_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-reject-001", ct);
        await sm.MarkRejectedAsync(request.Id, "E16: Supplier already holds this metering point", ct);

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "rejected", "rejection_reason");

        var rejectionEvent = events.First(e => e.EventType == "rejection_reason");
        rejectionEvent.Payload.Should().Contain("Supplier already holds this metering point");

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("rejected");
    }

    // ══════════════════════════════════════════════════════════════
    //  CANCELLATION (after acknowledged):
    //  created → sent → acknowledged → awaiting_effectuation → cancellation_sent → cancelled + cancellation_reason
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cancellation_after_acknowledgment_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-cancel-001", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);

        // BRS-003 sent → cancellation_pending
        await sm.MarkCancellationSentAsync(request.Id, ct);

        var midProcess = await _processRepo.GetAsync(request.Id, ct);
        midProcess!.Status.Should().Be("cancellation_pending");

        // DataHub acknowledges cancellation
        await sm.MarkCancelledAsync(request.Id, "Cancellation acknowledged by DataHub", ct);

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation",
            "cancellation_sent", "cancelled", "cancellation_reason");
        events.Should().HaveCount(7);

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("cancelled");
    }

    // ══════════════════════════════════════════════════════════════
    //  CANCELLATION (before sent): created → cancelled + cancellation_reason
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cancellation_before_sending_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkCancelledAsync(request.Id, "Cancelled by user", ct);

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "cancelled", "cancellation_reason");
        events.Should().HaveCount(3);

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("cancelled");
    }

    // ══════════════════════════════════════════════════════════════
    //  FULL LIFECYCLE: sunshine path → offboarding → final_settled
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Full_lifecycle_to_final_settlement_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-full-001", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);
        await sm.MarkCompletedAsync(request.Id, ct);
        await sm.MarkOffboardingAsync(request.Id, ct);
        await sm.MarkFinalSettledAsync(request.Id, ct);

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation",
            "completed", "offboarding_started", "final_settled");
        events.Should().HaveCount(7);

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("final_settled");
    }

    // ══════════════════════════════════════════════════════════════
    //  VERIFY: Timestamps are monotonically increasing
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Event_timestamps_are_monotonically_increasing()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-ts-001", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);
        await sm.MarkCompletedAsync(request.Id, ct);

        var events = await _processRepo.GetEventsAsync(request.Id, ct);

        for (int i = 1; i < events.Count; i++)
        {
            events[i].OccurredAt.Should().BeOnOrAfter(events[i - 1].OccurredAt,
                $"event '{events[i].EventType}' should not precede '{events[i - 1].EventType}'");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  QUEUE POLLER: RSM-001 acceptance triggers acknowledge + awaiting_effectuation
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueuePoller_RSM001_accepted_transitions_to_effectuation_pending()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-poller-001", ct);

        var parser = new StubCimParser(new Rsm001ResponseResult("corr-poller-001", true, null, null));
        var poller = CreatePoller(parser);

        _dataHubClient.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm001-001", "RSM-001", "corr-poller-001", "{}"));

        await poller.PollQueueAsync(QueueName.MasterData, ct);

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("effectuation_pending");

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation");
    }

    // ══════════════════════════════════════════════════════════════
    //  QUEUE POLLER: RSM-001 rejection triggers rejected + rejection_reason
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueuePoller_RSM001_rejected_transitions_to_rejected()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-poller-002", ct);

        var parser = new StubCimParser(new Rsm001ResponseResult("corr-poller-002", false, "E16: Invalid GSRN", "E16"));
        var poller = CreatePoller(parser);

        _dataHubClient.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm001-002", "RSM-001", "corr-poller-002", "{}"));

        await poller.PollQueueAsync(QueueName.MasterData, ct);

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("rejected");

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Should().Contain(e => e.EventType == "rejection_reason");
    }

    // ══════════════════════════════════════════════════════════════
    //  QUEUE POLLER: RSM-001 for cancellation ack via cancel_correlation_id
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueuePoller_RSM001_cancellation_ack_transitions_to_cancelled()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-poller-003", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);
        await sm.MarkCancellationSentAsync(request.Id, ct);

        var processBefore = await _processRepo.GetAsync(request.Id, ct);
        processBefore!.Status.Should().Be("cancellation_pending");

        // RSM-001 arrives with the same original correlation ID (status-based disambiguation)
        var parser = new StubCimParser(new Rsm001ResponseResult("corr-poller-003", true, null, null));
        var poller = CreatePoller(parser);

        _dataHubClient.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm001-cancel", "RSM-001", "corr-poller-003", "{}"));

        await poller.PollQueueAsync(QueueName.MasterData, ct);

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("cancelled");

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation",
            "cancellation_sent", "cancelled");
    }

    // ══════════════════════════════════════════════════════════════
    //  STATE MACHINE: cancellation_pending → effectuation_pending via RevertCancellationAsync
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cancellation_rejected_reverts_to_effectuation_pending()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-revert-001", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);
        await sm.MarkCancellationSentAsync(request.Id, ct);

        var midProcess = await _processRepo.GetAsync(request.Id, ct);
        midProcess!.Status.Should().Be("cancellation_pending");

        // DataHub rejects the cancellation
        await sm.RevertCancellationAsync(request.Id, "E47: Cancellation deadline exceeded", ct);

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation",
            "cancellation_sent", "cancellation_rejected", "cancellation_rejection_reason");

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("effectuation_pending");
    }

    // ══════════════════════════════════════════════════════════════
    //  QUEUE POLLER: RSM-001 cancellation rejection via cancel_correlation_id
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueuePoller_RSM001_cancellation_rejected_reverts_to_effectuation_pending()
    {
        var ct = CancellationToken.None;
        var sm = CreateStateMachine();

        var request = await sm.CreateRequestAsync("571313100000012345", "supplier_switch", new DateOnly(2025, 2, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-poller-004", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);
        await sm.MarkCancellationSentAsync(request.Id, ct);

        var processBefore = await _processRepo.GetAsync(request.Id, ct);
        processBefore!.Status.Should().Be("cancellation_pending");

        // RSM-001 arrives with same original correlation ID but Accepted=false (status-based disambiguation)
        var parser = new StubCimParser(new Rsm001ResponseResult("corr-poller-004", false, "E47: Cancellation deadline exceeded", "E47"));
        var poller = CreatePoller(parser);

        _dataHubClient.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm001-cancel-reject", "RSM-001", "corr-poller-004", "{}"));

        await poller.PollQueueAsync(QueueName.MasterData, ct);

        var process = await _processRepo.GetAsync(request.Id, ct);
        process!.Status.Should().Be("effectuation_pending");

        var events = await _processRepo.GetEventsAsync(request.Id, ct);
        events.Should().Contain(e => e.EventType == "cancellation_rejected");
        events.Should().Contain(e => e.EventType == "cancellation_rejection_reason");
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers — QueuePollerService wiring with no-op stubs
    // ══════════════════════════════════════════════════════════════

    private QueuePollerService CreatePoller(ICimParser parser) =>
        new(
            _dataHubClient, parser,
            new ThrowMeteringRepo(),
            new ThrowPortfolioRepo(),
            _processRepo,
            new ThrowSignupRepo(),
            NullOnboardingService.Instance,
            new ThrowTariffRepo(),
            new ThrowBrsBuilder(),
            new ThrowMessageRepo(),
            _clock,
            new NullMessageLog(),
            new NullInvoiceService(),
            NullLogger<QueuePollerService>.Instance);

    /// <summary>Stub parser that returns a preconfigured RSM-001 result.</summary>
    private sealed class StubCimParser(Rsm001ResponseResult rsm001ResponseResult) : ICimParser
    {
        public Rsm001ResponseResult ParseRsm001Response(string json) => rsm001ResponseResult;
        public IReadOnlyList<ParsedTimeSeries> ParseRsm012(string json) => throw new NotImplementedException();
        public Domain.MasterData.ParsedMasterData ParseRsm022(string json) => throw new NotImplementedException();
        public Rsm004Result ParseRsm004(string json) => throw new NotImplementedException();
        public Rsm014Aggregation ParseRsm014(string json) => throw new NotImplementedException();
        public Rsm001ResponseResult ParseRsm005Response(string json) => throw new NotImplementedException();
        public Rsm001ResponseResult ParseRsm024Response(string json) => throw new NotImplementedException();
        public Rsm028Result ParseRsm028(string json) => throw new NotImplementedException();
        public Rsm031Result ParseRsm031(string json) => throw new NotImplementedException();
    }

    /// <summary>No-op IMessageLog for tests.</summary>
    private sealed class NullMessageLog : IMessageLog
    {
        public Task<bool> IsProcessedAsync(string messageId, CancellationToken ct) => Task.FromResult(false);
        public Task MarkProcessedAsync(string messageId, CancellationToken ct) => Task.CompletedTask;
        public Task RecordInboundAsync(string messageId, string messageType, string? correlationId, string queueName, int payloadSize, string rawPayload, CancellationToken ct) => Task.CompletedTask;
        public Task MarkInboundStatusAsync(string messageId, string status, string? errorDetails, CancellationToken ct) => Task.CompletedTask;
        public Task DeadLetterAsync(string messageId, string queueName, string errorReason, string rawPayload, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Throws on all methods — QueuePoller RSM-001 path never touches metering.</summary>
    private sealed class ThrowMeteringRepo : IMeteringDataRepository
    {
        public Task StoreTimeSeriesAsync(string gsrn, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> StoreTimeSeriesWithHistoryAsync(string gsrn, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<MeteringDataRow>> GetConsumptionAsync(string gsrn, DateTime from, DateTime to, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<MeteringDataChange>> GetChangesAsync(string gsrn, DateTime from, DateTime to, CancellationToken ct) => throw new NotImplementedException();
    }

    /// <summary>Throws on all methods — QueuePoller RSM-001 path never touches portfolio.</summary>
    private sealed class ThrowPortfolioRepo : IPortfolioRepository
    {
        public Task<Customer> CreateCustomerAsync(string name, string cprCvr, string contactType, Address? billingAddress, CancellationToken ct) => throw new NotImplementedException();
        public Task<Customer?> GetCustomerByCprCvrAsync(string cprCvr, CancellationToken ct) => throw new NotImplementedException();
        public Task<MeteringPoint> CreateMeteringPointAsync(MeteringPoint mp, CancellationToken ct) => throw new NotImplementedException();
        public Task<Product> CreateProductAsync(string name, string energyModel, decimal marginOrePerKwh, decimal? supplementOrePerKwh, decimal subscriptionKrPerMonth, CancellationToken ct) => throw new NotImplementedException();
        public Task<Contract> CreateContractAsync(Guid customerId, string gsrn, Guid productId, string billingFrequency, string paymentModel, DateOnly startDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<SupplyPeriod> CreateSupplyPeriodAsync(string gsrn, DateOnly startDate, CancellationToken ct) => throw new NotImplementedException();
        public Task ActivateMeteringPointAsync(string gsrn, DateTime activatedAtUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task<Contract?> GetActiveContractAsync(string gsrn, CancellationToken ct) => throw new NotImplementedException();
        public Task<Product?> GetProductAsync(Guid productId, CancellationToken ct) => throw new NotImplementedException();
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
        public Task<MeteringPoint?> GetMeteringPointByGsrnAsync(string gsrn, CancellationToken ct) => throw new NotImplementedException();
        public Task StageCustomerDataAsync(string gsrn, string customerName, string? cprCvr, string customerType, string? phone, string? email, string? correlationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<StagedCustomerData?> GetStagedCustomerDataAsync(string gsrn, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ThrowTariffRepo : Application.Tariff.ITariffRepository
    {
        public Task<IReadOnlyList<Application.Tariff.TariffRateRow>> GetRatesAsync(string gridAreaCode, string tariffType, DateOnly date, CancellationToken ct) => throw new NotImplementedException();
        public Task<decimal> GetSubscriptionAsync(string gridAreaCode, string subscriptionType, DateOnly date, CancellationToken ct) => throw new NotImplementedException();
        public Task<decimal> GetElectricityTaxAsync(DateOnly date, CancellationToken ct) => throw new NotImplementedException();
        public Task SeedGridTariffAsync(string gridAreaCode, string tariffType, DateOnly validFrom, IReadOnlyList<Application.Tariff.TariffRateRow> rates, CancellationToken ct) => throw new NotImplementedException();
        public Task SeedSubscriptionAsync(string gridAreaCode, string subscriptionType, decimal amountPerMonth, DateOnly validFrom, CancellationToken ct) => throw new NotImplementedException();
        public Task SeedElectricityTaxAsync(decimal ratePerKwh, DateOnly validFrom, CancellationToken ct) => throw new NotImplementedException();
        public Task StoreTariffAttachmentsAsync(string gsrn, IReadOnlyList<Application.Parsing.TariffAttachment> tariffs, string? correlationId, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ThrowBrsBuilder : Application.DataHub.IBrsRequestBuilder
    {
        public string BuildBrs001(string gsrn, string cprCvr, DateOnly effectiveDate) => throw new NotImplementedException();
        public string BuildBrs002(string gsrn, DateOnly effectiveDate) => throw new NotImplementedException();
        public string BuildBrs003(string gsrn, string originalCorrelationId) => throw new NotImplementedException();
        public string BuildBrs009(string gsrn, string cprCvr, DateOnly effectiveDate) => throw new NotImplementedException();
        public string BuildBrs010(string gsrn, DateOnly effectiveDate) => throw new NotImplementedException();
        public string BuildBrs044(string gsrn, string originalCorrelationId) => throw new NotImplementedException();
        public string BuildRsm027(string gsrn, string customerName, string cprCvr, string correlationId) => throw new NotImplementedException();
    }

    private sealed class ThrowMessageRepo : Application.Messaging.IMessageRepository
    {
        public Task<Application.Common.PagedResult<Application.Messaging.InboundMessageSummary>> GetInboundMessagesAsync(Application.Messaging.MessageFilter filter, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Messaging.InboundMessageDetail?> GetInboundMessageAsync(Guid messageId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Common.PagedResult<Application.Messaging.OutboundRequestSummary>> GetOutboundRequestsAsync(Application.Messaging.OutboundFilter filter, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Messaging.OutboundRequestDetail?> GetOutboundRequestAsync(Guid requestId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Common.PagedResult<Application.Messaging.DeadLetterSummary>> GetDeadLettersAsync(bool? resolvedOnly, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Messaging.DeadLetterDetail?> GetDeadLetterAsync(Guid deadLetterId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Messaging.MessageStats> GetMessageStatsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Common.PagedResult<Application.Messaging.ConversationSummary>> GetConversationsAsync(int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<Application.Messaging.ConversationDetail?> GetConversationAsync(string correlationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Application.Messaging.DataDeliverySummary>> GetDataDeliveriesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task RecordOutboundRequestAsync(string processType, string gsrn, string correlationId, string status, string? rawPayload, CancellationToken ct) => throw new NotImplementedException();
    }

    /// <summary>Throws on all methods — QueuePoller RSM-001 path never touches signup (directly).</summary>
    private sealed class ThrowSignupRepo : ISignupRepository
    {
        public Task<Signup> CreateAsync(string signupNumber, string darId, string gsrn, string customerName, string customerCprCvr, string customerContactType, Guid productId, Guid processRequestId, string type, DateOnly effectiveDate, Guid? correctedFromId, SignupAddressInfo? addressInfo, string? mobile, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> NextSignupNumberAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<Signup?> GetBySignupNumberAsync(string signupNumber, CancellationToken ct) => throw new NotImplementedException();
        public Task<Signup?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<Signup?> GetByProcessRequestIdAsync(Guid processRequestId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Signup?> GetActiveByGsrnAsync(string gsrn, CancellationToken ct) => Task.FromResult<Signup?>(null);
        public Task UpdateStatusAsync(Guid id, string status, string? rejectionReason, CancellationToken ct) => throw new NotImplementedException();
        public Task SetProcessRequestIdAsync(Guid id, Guid processRequestId, CancellationToken ct) => throw new NotImplementedException();
        public Task LinkCustomerAsync(Guid signupId, Guid customerId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetCustomerCprCvrAsync(Guid signupId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SignupListItem>> GetAllAsync(string? statusFilter, CancellationToken ct) => throw new NotImplementedException();
        public Task<PagedResult<SignupListItem>> GetAllPagedAsync(string? statusFilter, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SignupListItem>> GetRecentAsync(int limit, CancellationToken ct) => throw new NotImplementedException();
        public Task<SignupDetail?> GetDetailByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SignupCorrectionLink>> GetCorrectionChainAsync(Guid signupId, CancellationToken ct) => throw new NotImplementedException();
        public Task<SignupAddressInfo?> GetAddressInfoAsync(Guid signupId, CancellationToken ct) => throw new NotImplementedException();
    }
}
