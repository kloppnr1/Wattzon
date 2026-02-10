using System.Text.Json;
using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Parsing;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// End-to-end integration tests that verify the process timeline events
/// are created in the correct order for all known signup lifecycle scenarios.
/// Uses FakeDataHubClient to deterministically drive the pipeline without
/// waiting for real simulator delays.
/// </summary>
[Collection("Database")]
public class ProcessTimelineTests
{
    private static int _gsrnCounter;

    private readonly PortfolioRepository _portfolioRepo;
    private readonly ProcessRepository _processRepo;
    private readonly SignupRepository _signupRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly MessageLog _messageLog;

    public ProcessTimelineTests(TestDatabase db)
    {
        _portfolioRepo = new PortfolioRepository(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        _signupRepo = new SignupRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _messageLog = new MessageLog(TestDatabase.ConnectionString);
    }

    private static string NextGsrn()
    {
        var n = Interlocked.Increment(ref _gsrnCounter);
        return $"57131310000001{n:D4}";
    }

    private static string LoadRsm007Fixture(string gsrn)
    {
        var json = File.ReadAllText(Path.Combine("..", "..", "..", "..", "..", "fixtures", "rsm007-activation.json"));
        return json.Replace("571313100000012345", gsrn);
    }

    private static string BuildRsm009Accepted(string correlationId) =>
        JsonSerializer.Serialize(new
        {
            MarketDocument = new
            {
                mRID = correlationId,
                MktActivityRecord = new
                {
                    status = new { value = "A01" },
                },
            },
        });

    private static string BuildRsm009Rejected(string correlationId, string code, string reason) =>
        JsonSerializer.Serialize(new
        {
            MarketDocument = new
            {
                mRID = correlationId,
                MktActivityRecord = new
                {
                    status = new { value = "A02" },
                    Reason = new { code, text = reason },
                },
            },
        });

    private async Task<(OnboardingService onboarding, FakeDataHubClient client, QueuePollerService poller, ProcessSchedulerService scheduler, TestClock clock)>
        SetupServicesAsync(string gsrn, CancellationToken ct)
    {
        await _portfolioRepo.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);
        await _portfolioRepo.CreateProductAsync("Spot Test", "spot", 0.04m, null, 39.00m, ct);

        var client = new FakeDataHubClient();
        var clock = new TestClock { Today = new DateOnly(2024, 12, 5) };
        var addressLookup = new StubAddressLookupClient(gsrn);
        var brsBuilder = new BrsRequestBuilder();
        var messageRepo = new NullMessageRepository();
        var parser = new CimJsonParser();

        var onboarding = new OnboardingService(
            _signupRepo, _portfolioRepo, _processRepo,
            addressLookup, client, brsBuilder, messageRepo, clock,
            NullLogger<OnboardingService>.Instance);

        var poller = new QueuePollerService(
            client, parser, _meteringRepo, _portfolioRepo, _processRepo, _signupRepo,
            onboarding, clock, _messageLog,
            NullLogger<QueuePollerService>.Instance);

        var scheduler = new ProcessSchedulerService(
            _processRepo, _signupRepo, client, brsBuilder,
            onboarding, messageRepo, clock,
            NullLogger<ProcessSchedulerService>.Instance);

        return (onboarding, client, poller, scheduler, clock);
    }

    private async Task<SignupResponse> CreateTestSignupAsync(
        OnboardingService onboarding, string cprCvr, string gsrn, CancellationToken ct)
    {
        var product = (await _portfolioRepo.GetActiveProductsAsync(ct))[0];

        return await onboarding.CreateSignupAsync(new SignupRequest(
            DarId: "test-dar",
            CustomerName: "Timeline Test Customer",
            CprCvr: cprCvr,
            ContactType: "person",
            Email: "test@example.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: new DateOnly(2025, 1, 1),
            Gsrn: gsrn), ct);
    }

    private async Task<IReadOnlyList<ProcessEvent>> GetEventsAsync(Guid processRequestId, CancellationToken ct)
    {
        return await _processRepo.GetEventsAsync(processRequestId, ct);
    }

    // ══════════════════════════════════════════════════════════════
    //  SUNSHINE PATH: created → sent → acknowledged → awaiting_effectuation → completed
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Sunshine_path_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var gsrn = NextGsrn();
        var (onboarding, client, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup → "created" event
        var response = await CreateTestSignupAsync(onboarding, "1111111111", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        var events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("created");

        // 2. Scheduler sends to DataHub → "sent" event
        await scheduler.RunTickAsync(ct);

        events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(2);
        events[1].EventType.Should().Be("sent");

        // Get the correlation ID assigned by FakeDataHubClient
        var process = await _processRepo.GetAsync(processId, ct);
        process!.Status.Should().Be("sent_to_datahub");
        process.DatahubCorrelationId.Should().NotBeNullOrEmpty();

        // 3. RSM-009 accepted → "acknowledged" + "awaiting_effectuation" events
        client.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm009", "RSM-009", process.DatahubCorrelationId,
                BuildRsm009Accepted(process.DatahubCorrelationId!)));

        await poller.PollQueueAsync(QueueName.MasterData, ct);

        events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(4);
        events[2].EventType.Should().Be("acknowledged");
        events[3].EventType.Should().Be("awaiting_effectuation");

        process = await _processRepo.GetAsync(processId, ct);
        process!.Status.Should().Be("effectuation_pending");

        // 4. RSM-007 activation → "completed" event
        client.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm007", "RSM-007", process.DatahubCorrelationId, LoadRsm007Fixture(gsrn)));

        await poller.PollQueueAsync(QueueName.MasterData, ct);

        events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(5);
        events[4].EventType.Should().Be("completed");

        // Verify final signup status
        signup = await _signupRepo.GetByIdAsync(signup.Id, ct);
        signup!.Status.Should().Be("active");
        signup.CustomerId.Should().NotBeNull();

        // Verify complete event order
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation", "completed");

        // Verify timestamps are monotonically increasing
        for (int i = 1; i < events.Count; i++)
        {
            events[i].OccurredAt.Should().BeOnOrAfter(events[i - 1].OccurredAt,
                $"event '{events[i].EventType}' should not precede '{events[i - 1].EventType}'");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  REJECTION PATH: created → sent → rejected + rejection_reason
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Rejection_path_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var gsrn = NextGsrn();
        var (onboarding, client, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup
        var response = await CreateTestSignupAsync(onboarding, "2222222222", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // 2. Send to DataHub
        await scheduler.RunTickAsync(ct);
        var process = await _processRepo.GetAsync(processId, ct);

        // 3. RSM-009 rejected
        client.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm009-reject", "RSM-009", process!.DatahubCorrelationId,
                BuildRsm009Rejected(process.DatahubCorrelationId!, "E16", "Invalid GSRN checksum")));

        await poller.PollQueueAsync(QueueName.MasterData, ct);

        var events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(4);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "rejected", "rejection_reason");

        // Verify rejection reason is stored in payload
        var rejectionEvent = events.First(e => e.EventType == "rejection_reason");
        rejectionEvent.Payload.Should().Contain("Invalid GSRN checksum");

        // Verify final status
        process = await _processRepo.GetAsync(processId, ct);
        process!.Status.Should().Be("rejected");

        signup = await _signupRepo.GetByIdAsync(signup.Id, ct);
        signup!.Status.Should().Be("rejected");
        signup.CustomerId.Should().BeNull("rejected signup should not create a customer");
    }

    // ══════════════════════════════════════════════════════════════
    //  CANCELLATION (after acknowledged): created → sent → acknowledged → awaiting_effectuation → cancelled + cancellation_reason
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cancellation_after_acknowledgment_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var gsrn = NextGsrn();
        var (onboarding, client, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup
        var response = await CreateTestSignupAsync(onboarding, "3333333333", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // 2. Send to DataHub
        await scheduler.RunTickAsync(ct);
        var process = await _processRepo.GetAsync(processId, ct);

        // 3. RSM-009 accepted
        client.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm009-ack", "RSM-009", process!.DatahubCorrelationId,
                BuildRsm009Accepted(process.DatahubCorrelationId!)));
        await poller.PollQueueAsync(QueueName.MasterData, ct);

        // 4. User cancels while awaiting effectuation
        await onboarding.CancelAsync(response.SignupId, ct);

        var events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(6);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation", "cancelled", "cancellation_reason");

        // Verify final status
        process = await _processRepo.GetAsync(processId, ct);
        process!.Status.Should().Be("cancelled");

        signup = await _signupRepo.GetByIdAsync(signup.Id, ct);
        signup!.Status.Should().Be("cancelled");
    }

    // ══════════════════════════════════════════════════════════════
    //  CANCELLATION (before sent): created → cancelled + cancellation_reason
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cancellation_before_sending_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var gsrn = NextGsrn();
        var (onboarding, client, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup
        var response = await CreateTestSignupAsync(onboarding, "4444444444", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // 2. Cancel immediately (before scheduler runs)
        await onboarding.CancelAsync(response.SignupId, ct);

        var events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(3);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "cancelled", "cancellation_reason");

        // Verify scheduler does NOT pick up cancelled process
        await scheduler.RunTickAsync(ct);

        events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(3, "scheduler should not add events to cancelled process");

        var process = await _processRepo.GetAsync(processId, ct);
        process!.Status.Should().Be("cancelled");
    }

    // ══════════════════════════════════════════════════════════════
    //  FULL LIFECYCLE: sunshine path → offboarding → final_settled
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Full_lifecycle_to_final_settlement_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var gsrn = NextGsrn();
        var (onboarding, client, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup and advance through sunshine path
        var response = await CreateTestSignupAsync(onboarding, "5555555555", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // Send → Acknowledge → Complete
        await scheduler.RunTickAsync(ct);
        var process = await _processRepo.GetAsync(processId, ct);

        client.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm009-full", "RSM-009", process!.DatahubCorrelationId,
                BuildRsm009Accepted(process.DatahubCorrelationId!)));
        await poller.PollQueueAsync(QueueName.MasterData, ct);

        client.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm007-full", "RSM-007", process.DatahubCorrelationId, LoadRsm007Fixture(gsrn)));
        await poller.PollQueueAsync(QueueName.MasterData, ct);

        // 2. Offboarding
        var stateMachine = new ProcessStateMachine(_processRepo, clock);
        await stateMachine.MarkOffboardingAsync(processId, ct);

        // 3. Final settlement
        await stateMachine.MarkFinalSettledAsync(processId, ct);

        var events = await GetEventsAsync(processId, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation",
            "completed", "offboarding_started", "final_settled");

        events.Should().HaveCount(7);

        // Verify timestamps are monotonically increasing
        for (int i = 1; i < events.Count; i++)
        {
            events[i].OccurredAt.Should().BeOnOrAfter(events[i - 1].OccurredAt,
                $"event '{events[i].EventType}' should not precede '{events[i - 1].EventType}'");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  VERIFY: Scheduler does not re-send already sent processes
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Scheduler_only_sends_pending_processes()
    {
        var ct = CancellationToken.None;
        var gsrn = NextGsrn();
        var (onboarding, client, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        var response = await CreateTestSignupAsync(onboarding, "6666666666", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // First tick sends the process
        await scheduler.RunTickAsync(ct);
        var events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(2); // created + sent

        // Second tick should not add more events
        await scheduler.RunTickAsync(ct);
        events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(2, "scheduler should not re-send an already sent process");
    }

    /// <summary>
    /// Stub that returns the GSRN used in the RSM-007 fixture for any DAR ID.
    /// </summary>
    private sealed class StubAddressLookupClient(string gsrn) : IAddressLookupClient
    {
        public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
        {
            return Task.FromResult(new AddressLookupResult(new List<MeteringPointInfo>
            {
                new(gsrn, "E17", "344")
            }));
        }
    }
}
