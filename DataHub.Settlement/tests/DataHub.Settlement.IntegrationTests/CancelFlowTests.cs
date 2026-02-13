using Dapper;
using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Parsing;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// Tests for the RSM-024 cancel flow and RSM-004/D11 auto-cancel via QueuePollerService.
/// </summary>
[Collection("Database")]
public class CancelFlowTests
{
    private const string Gsrn = "571313100000067890";

    private readonly PortfolioRepository _portfolio;
    private readonly ProcessRepository _processRepo;
    private readonly SignupRepository _signupRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly MessageLog _messageLog;

    public CancelFlowTests(TestDatabase db)
    {
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        _signupRepo = new SignupRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _messageLog = new MessageLog(TestDatabase.ConnectionString);
    }

    [Fact]
    public async Task Cancel_supplier_switch_before_effectuation_cancels_process()
    {
        var ct = CancellationToken.None;
        var clock = new TestClock { Today = new DateOnly(2024, 12, 5) };

        // ──── 0. CLEANUP ────
        await CleanupAsync(ct);

        // ──── 1. ARRANGE: grid area + product + signup ────
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);
        var product = await _portfolio.CreateProductAsync("Cancel Test Spot", "spot", 4.0m, null, 39.00m, ct);

        var fakeClient = new FakeDataHubClient();
        var onboardingService = new OnboardingService(
            _signupRepo, _portfolio, _processRepo,
            new StubAddressLookupClient(), fakeClient,
            new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(), clock,
            NullLogger<OnboardingService>.Instance);

        var signupRequest = new SignupRequest(
            DarId: "test-dar-cancel",
            CustomerName: "Cancel Test Customer",
            CprCvr: "8888888888",
            ContactType: "person",
            Email: "cancel@test.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: new DateOnly(2025, 1, 1),
            Mobile: "+4512345678",
            Gsrn: Gsrn
        );

        var signupResponse = await onboardingService.CreateSignupAsync(signupRequest, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(signupResponse.SignupId, ct);
        signup.Should().NotBeNull();

        // ──── 2. Advance to effectuation_pending ────
        var stateMachine = new ProcessStateMachine(_processRepo, clock);
        var correlationId = "corr-cancel-test";
        await stateMachine.MarkSentAsync(signup!.ProcessRequestId!.Value, correlationId, ct);
        await stateMachine.MarkAcknowledgedAsync(signup.ProcessRequestId.Value, ct);

        var processBeforeCancel = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
        processBeforeCancel!.Status.Should().Be("effectuation_pending");

        // ──── 3. Send RSM-024 cancel (uses same correlation ID) ────
        await stateMachine.MarkCancellationSentAsync(signup.ProcessRequestId.Value, ct);

        var processCancelling = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
        processCancelling!.Status.Should().Be("cancellation_pending");

        // ──── 4. Enqueue RSM-001 ack (cancel confirmation) and process via QueuePoller ────
        var rsm001CancelAckJson = BuildRsm001AcceptedJson(correlationId);
        fakeClient.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-cancel-ack", "RSM-001", correlationId, rsm001CancelAckJson));

        var effectuationService = new EffectuationService(
            TestDatabase.ConnectionString, onboardingService, new NullInvoiceService(),
            fakeClient, new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(), clock, NullLogger<EffectuationService>.Instance);

        var parser = new CimJsonParser();
        var poller = new QueuePollerService(
            fakeClient, parser, _meteringRepo, _portfolio, _processRepo, _signupRepo,
            onboardingService,
            new Infrastructure.Tariff.TariffRepository(TestDatabase.ConnectionString),
            new Infrastructure.DataHub.BrsRequestBuilder(), new NullMessageRepository(),
            clock, _messageLog,
            new NullInvoiceService(),
            effectuationService,
            NullLogger<QueuePollerService>.Instance);

        var processed = await poller.PollQueueAsync(QueueName.MasterData, ct);
        processed.Should().BeTrue();

        // ──── 5. ASSERT: process cancelled ────
        var processAfter = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
        processAfter!.Status.Should().Be("cancelled");

        // ──── 6. ASSERT: message dequeued ────
        var peek = await fakeClient.PeekAsync(QueueName.MasterData, ct);
        peek.Should().BeNull();
    }

    [Fact]
    public async Task RSM004_D11_auto_cancel_cancels_effectuation_pending_process()
    {
        var ct = CancellationToken.None;
        var clock = new TestClock { Today = new DateOnly(2024, 12, 5) };

        // ──── 0. CLEANUP ────
        await CleanupAsync(ct);

        // ──── 1. ARRANGE: grid area + product + signup ────
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);
        var product = await _portfolio.CreateProductAsync("D11 Test Spot", "spot", 4.0m, null, 39.00m, ct);

        var fakeClient = new FakeDataHubClient();
        var onboardingService = new OnboardingService(
            _signupRepo, _portfolio, _processRepo,
            new StubAddressLookupClient(), fakeClient,
            new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(), clock,
            NullLogger<OnboardingService>.Instance);

        var signupRequest = new SignupRequest(
            DarId: "test-dar-d11",
            CustomerName: "D11 Test Customer",
            CprCvr: "7777777777",
            ContactType: "person",
            Email: "d11@test.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: new DateOnly(2025, 2, 1),
            Mobile: "+4512345678",
            Gsrn: Gsrn
        );

        var signupResponse = await onboardingService.CreateSignupAsync(signupRequest, ct);
        var signup = await _signupRepo.GetBySignupNumberAsync(signupResponse.SignupId, ct);
        signup.Should().NotBeNull();

        // ──── 2. Advance to effectuation_pending ────
        var stateMachine = new ProcessStateMachine(_processRepo, clock);
        await stateMachine.MarkSentAsync(signup!.ProcessRequestId!.Value, "corr-d11-test", ct);
        await stateMachine.MarkAcknowledgedAsync(signup.ProcessRequestId.Value, ct);

        var processBefore = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
        processBefore!.Status.Should().Be("effectuation_pending");

        // ──── 3. Enqueue RSM-004/D11 and process via QueuePoller ────
        var rsm004D11Json = BuildRsm004D11Json(Gsrn);

        fakeClient.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-d11-test", "RSM-004", null, rsm004D11Json));

        var effectuationService = new EffectuationService(
            TestDatabase.ConnectionString, onboardingService, new NullInvoiceService(),
            fakeClient, new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(), clock, NullLogger<EffectuationService>.Instance);

        var parser = new CimJsonParser();
        var poller = new QueuePollerService(
            fakeClient, parser, _meteringRepo, _portfolio, _processRepo, _signupRepo,
            onboardingService,
            new Infrastructure.Tariff.TariffRepository(TestDatabase.ConnectionString),
            new Infrastructure.DataHub.BrsRequestBuilder(), new NullMessageRepository(),
            clock, _messageLog,
            new NullInvoiceService(),
            effectuationService,
            NullLogger<QueuePollerService>.Instance);

        var processed = await poller.PollQueueAsync(QueueName.MasterData, ct);
        processed.Should().BeTrue();

        // ──── 4. ASSERT: process cancelled (bypasses cancellation_pending) ────
        var processAfter = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
        processAfter!.Status.Should().Be("cancelled");

        // ──── 5. ASSERT: cancellation event recorded ────
        var events = await _processRepo.GetEventsAsync(signup.ProcessRequestId.Value, ct);
        events.Should().Contain(e => e.EventType == "auto_cancelled");
        events.Should().Contain(e => e.EventType == "cancellation_reason"
            && e.Payload != null && e.Payload.Contains("D11"));
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM portfolio.contract WHERE gsrn = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.supply_period WHERE gsrn = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.signup WHERE gsrn = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM lifecycle.process_event WHERE process_request_id IN (SELECT id FROM lifecycle.process_request WHERE gsrn = @Gsrn)", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM lifecycle.process_request WHERE gsrn = @Gsrn", new { Gsrn });
    }

    private static string BuildRsm001AcceptedJson(string correlationId)
    {
        return $$"""
        {
          "MarketDocument": {
            "mRID": "{{correlationId}}",
            "type": "E44",
            "MktActivityRecord": {
              "status": { "value": "A01" }
            }
          }
        }
        """;
    }

    private static string BuildRsm004D11Json(string gsrn)
    {
        return $$"""
        {
          "MarketDocument": {
            "mRID": "msg-rsm004-d11-001",
            "type": "E44",
            "Process": { "ProcessType": "E32" },
            "MktActivityRecord": {
              "MarketEvaluationPoint": {
                "mRID": "{{gsrn}}"
              },
              "Reason": {
                "code": "D11",
                "text": "Customer data deadline exceeded"
              },
              "Period": {
                "timeInterval": {
                  "start": "2025-02-01T00:00:00Z"
                }
              }
            }
          }
        }
        """;
    }

    private sealed class StubAddressLookupClient : IAddressLookupClient
    {
        public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
        {
            return Task.FromResult(new AddressLookupResult(new List<MeteringPointInfo>
            {
                new(Gsrn, "E17", "344")
            }));
        }
    }
}
