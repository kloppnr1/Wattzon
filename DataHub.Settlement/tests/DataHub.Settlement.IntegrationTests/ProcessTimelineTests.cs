using System.Text;
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
using DataHub.Settlement.Simulator;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// End-to-end integration tests that verify the process timeline events
/// are created in the correct order for all known signup lifecycle scenarios.
/// Uses the DataHub Simulator (in-process via WebApplicationFactory) so that
/// BRS requests produce real RSM-009/RSM-007 responses through the queue.
/// </summary>
[Collection("Database")]
public class ProcessTimelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static int _gsrnCounter;

    private readonly WebApplicationFactory<Program> _simulatorFactory;
    private readonly PortfolioRepository _portfolioRepo;
    private readonly ProcessRepository _processRepo;
    private readonly SignupRepository _signupRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly MessageLog _messageLog;

    public ProcessTimelineTests(TestDatabase db, WebApplicationFactory<Program> simulatorFactory)
    {
        _simulatorFactory = simulatorFactory;
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

    private async Task<(OnboardingService onboarding, HttpClient admin, QueuePollerService poller, ProcessSchedulerService scheduler, TestClock clock)>
        SetupServicesAsync(string gsrn, CancellationToken ct)
    {
        await _portfolioRepo.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);
        await _portfolioRepo.CreateProductAsync("Spot Test", "spot", 0.04m, null, 39.00m, ct);

        var dataHubClient = new HttpDataHubClient(_simulatorFactory.CreateClient());
        var admin = _simulatorFactory.CreateClient();

        // Reset simulator state before each test
        await admin.PostAsync("/admin/reset", null);

        var clock = new TestClock { Today = new DateOnly(2024, 12, 5) };
        var addressLookup = new StubAddressLookupClient(gsrn);
        var brsBuilder = new BrsRequestBuilder();
        var messageRepo = new NullMessageRepository();
        var parser = new CimJsonParser();

        var onboarding = new OnboardingService(
            _signupRepo, _portfolioRepo, _processRepo,
            addressLookup, dataHubClient, brsBuilder, messageRepo, clock,
            NullLogger<OnboardingService>.Instance);

        var poller = new QueuePollerService(
            dataHubClient, parser, _meteringRepo, _portfolioRepo, _processRepo, _signupRepo,
            onboarding, clock, _messageLog,
            NullLogger<QueuePollerService>.Instance);

        var scheduler = new ProcessSchedulerService(
            _processRepo, _signupRepo, dataHubClient, brsBuilder,
            onboarding, messageRepo, clock,
            NullLogger<ProcessSchedulerService>.Instance);

        return (onboarding, admin, poller, scheduler, clock);
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
            EffectiveDate: new DateOnly(2027, 1, 1),
            Gsrn: gsrn), ct);
    }

    private async Task<IReadOnlyList<ProcessEvent>> GetEventsAsync(Guid processRequestId, CancellationToken ct)
    {
        return await _processRepo.GetEventsAsync(processRequestId, ct);
    }

    private static async Task AdminEnqueueAsync(
        HttpClient admin, string queue, string messageType, string? correlationId, string payload)
    {
        var json = JsonSerializer.Serialize(new { Queue = queue, MessageType = messageType, CorrelationId = correlationId, Payload = payload });
        var response = await admin.PostAsync("/admin/enqueue", new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
    }

    private static string BuildRsm007Json(string gsrn, string effectiveDate) =>
        JsonSerializer.Serialize(new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm007-{Guid.NewGuid():N}",
                type = "E44",
                MktActivityRecord = new
                {
                    MarketEvaluationPoint = new
                    {
                        mRID = gsrn,
                        type = "E17",
                        settlementMethod = "D01",
                        linkedMarketEvaluationPoint = new { mRID = "344" },
                        inDomain = new { mRID = "5790000392261" },
                    },
                    Period = new { timeInterval = new { start = effectiveDate } },
                },
            },
        });

    /// <summary>
    /// Polls the simulator queue and processes messages until the expected number
    /// of process events is reached, retrying every second up to the given timeout.
    /// This handles stale messages from previous tests gracefully.
    /// </summary>
    private async Task PollUntilEventCountAsync(
        QueuePollerService poller, QueueName queue, Guid processId, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            // Check if we already have the expected events
            var events = await _processRepo.GetEventsAsync(processId, CancellationToken.None);
            if (events.Count >= expectedCount)
                return;

            // Poll and process any available message
            await poller.PollQueueAsync(queue, CancellationToken.None);
            await Task.Delay(1_000);
        }

        var finalEvents = await _processRepo.GetEventsAsync(processId, CancellationToken.None);
        throw new TimeoutException(
            $"Expected {expectedCount} events but got {finalEvents.Count} on {queue} within {timeout.TotalSeconds}s. " +
            $"Events: [{string.Join(", ", finalEvents.Select(e => e.EventType))}]");
    }

    // ══════════════════════════════════════════════════════════════
    //  SUNSHINE PATH: created → sent → acknowledged → awaiting_effectuation → completed
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Sunshine_path_produces_correct_event_timeline()
    {
        var ct = CancellationToken.None;
        var gsrn = NextGsrn();
        var (onboarding, admin, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup → "created" event
        var response = await CreateTestSignupAsync(onboarding, "1111111111", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        var events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("created");

        // 2. Scheduler sends BRS-001 to simulator → "sent" event
        await scheduler.RunTickAsync(ct);

        events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(2);
        events[1].EventType.Should().Be("sent");

        var process = await _processRepo.GetAsync(processId, ct);
        process!.Status.Should().Be("sent_to_datahub");
        process.DatahubCorrelationId.Should().NotBeNullOrEmpty();

        // 3. Simulator auto-generates RSM-009 accepted after ~15s
        await PollUntilEventCountAsync(poller, QueueName.MasterData, processId, 4, TimeSpan.FromSeconds(30));

        events = await GetEventsAsync(processId, ct);
        events[2].EventType.Should().Be("acknowledged");
        events[3].EventType.Should().Be("awaiting_effectuation");

        process = await _processRepo.GetAsync(processId, ct);
        process!.Status.Should().Be("effectuation_pending");

        // 4. Enqueue RSM-007 via admin (simulating effective date arrival — future date prevents auto-flush)
        await AdminEnqueueAsync(admin, "MasterData", "RSM-007",
            process.DatahubCorrelationId, BuildRsm007Json(gsrn, "2027-01-01T00:00:00Z"));
        await PollUntilEventCountAsync(poller, QueueName.MasterData, processId, 5, TimeSpan.FromSeconds(10));

        events = await GetEventsAsync(processId, ct);
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
        var (onboarding, admin, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // Pre-activate GSRN so the simulator rejects the BRS request
        await admin.PostAsync($"/admin/activate/{gsrn}", null);

        // 1. Create signup
        var response = await CreateTestSignupAsync(onboarding, "2222222222", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // 2. Scheduler sends BRS-001 → simulator rejects (GSRN already active)
        await scheduler.RunTickAsync(ct);
        var process = await _processRepo.GetAsync(processId, ct);

        // 3. Simulator auto-generates RSM-009 rejection after ~15s
        await PollUntilEventCountAsync(poller, QueueName.MasterData, processId, 4, TimeSpan.FromSeconds(30));

        var events = await GetEventsAsync(processId, ct);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "rejected", "rejection_reason");

        // Verify rejection reason from simulator
        var rejectionEvent = events.First(e => e.EventType == "rejection_reason");
        rejectionEvent.Payload.Should().Contain("Supplier already holds this metering point");

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
        var (onboarding, admin, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup
        var response = await CreateTestSignupAsync(onboarding, "3333333333", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // 2. Scheduler sends BRS-001 to simulator
        await scheduler.RunTickAsync(ct);

        // 3. Wait for simulator RSM-009 accepted
        await PollUntilEventCountAsync(poller, QueueName.MasterData, processId, 4, TimeSpan.FromSeconds(30));

        var events = await GetEventsAsync(processId, ct);
        events[2].EventType.Should().Be("acknowledged");
        events[3].EventType.Should().Be("awaiting_effectuation");

        // 4. User cancels while awaiting effectuation (sends BRS-003 to simulator)
        await onboarding.CancelAsync(response.SignupId, ct);

        events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(6);
        events.Select(e => e.EventType).Should().ContainInOrder(
            "created", "sent", "acknowledged", "awaiting_effectuation", "cancelled", "cancellation_reason");

        // Verify final status
        var process = await _processRepo.GetAsync(processId, ct);
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
        var (onboarding, admin, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup
        var response = await CreateTestSignupAsync(onboarding, "4444444444", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // 2. Cancel immediately (before scheduler runs) — no simulator interaction
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
        var (onboarding, admin, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        // 1. Create signup and advance through sunshine path
        var response = await CreateTestSignupAsync(onboarding, "5555555555", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // Send BRS-001 to simulator
        await scheduler.RunTickAsync(ct);

        // Wait for RSM-009 accepted
        await PollUntilEventCountAsync(poller, QueueName.MasterData, processId, 4, TimeSpan.FromSeconds(30));

        // Enqueue RSM-007 via admin (future effective date prevents auto-flush)
        var process = await _processRepo.GetAsync(processId, ct);
        await AdminEnqueueAsync(admin, "MasterData", "RSM-007",
            process!.DatahubCorrelationId, BuildRsm007Json(gsrn, "2027-01-01T00:00:00Z"));
        await PollUntilEventCountAsync(poller, QueueName.MasterData, processId, 5, TimeSpan.FromSeconds(10));

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
        var (onboarding, admin, poller, scheduler, clock) = await SetupServicesAsync(gsrn, ct);

        var response = await CreateTestSignupAsync(onboarding, "6666666666", gsrn, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(response.SignupId, ct);
        var processId = signup!.ProcessRequestId!.Value;

        // First tick sends the process to simulator
        await scheduler.RunTickAsync(ct);
        var events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(2); // created + sent

        // Second tick should not add more events
        await scheduler.RunTickAsync(ct);
        events = await GetEventsAsync(processId, ct);
        events.Should().HaveCount(2, "scheduler should not re-send an already sent process");
    }

    /// <summary>
    /// Stub that returns the given GSRN for any DAR ID lookup.
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
