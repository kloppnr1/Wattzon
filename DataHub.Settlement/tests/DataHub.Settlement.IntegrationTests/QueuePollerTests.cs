using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Parsing;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.UnitTests;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class QueuePollerTests
{
    private readonly MeteringDataRepository _meteringRepo;
    private readonly PortfolioRepository _portfolioRepo;
    private readonly MessageLog _messageLog;

    public QueuePollerTests(TestDatabase db)
    {
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _portfolioRepo = new PortfolioRepository(TestDatabase.ConnectionString);
        _messageLog = new MessageLog(TestDatabase.ConnectionString);
    }

    private static string LoadSingleDayFixture() =>
        File.ReadAllText(Path.Combine("..", "..", "..", "..", "..", "fixtures", "rsm012-single-day.json"));

    private static string LoadRsm022Fixture() =>
        File.ReadAllText(Path.Combine("..", "..", "..", "..", "..", "fixtures", "rsm022-activation.json"));

    [Fact]
    public async Task Processes_rsm012_message_end_to_end()
    {
        var client = new FakeDataHubClient();
        var parser = new CimJsonParser();
        var processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        var signupRepo = new SignupRepository(TestDatabase.ConnectionString);
        var poller = new QueuePollerService(
            client, parser, _meteringRepo, _portfolioRepo, processRepo, signupRepo,
            NullOnboardingService.Instance, new TestClock(), _messageLog,
            NullLogger<QueuePollerService>.Instance);

        client.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-001", "RSM-012", null, LoadSingleDayFixture()));

        var processed = await poller.PollQueueAsync(QueueName.Timeseries, CancellationToken.None);

        processed.Should().BeTrue();

        // Message should be dequeued
        var peek = await client.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        peek.Should().BeNull();

        // Data should be stored
        var rows = await _meteringRepo.GetConsumptionAsync("571313100000012345",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);
        rows.Should().HaveCount(24);
    }

    [Fact]
    public async Task Duplicate_message_is_skipped()
    {
        var client = new FakeDataHubClient();
        var parser = new CimJsonParser();
        var processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        var signupRepo = new SignupRepository(TestDatabase.ConnectionString);
        var poller = new QueuePollerService(
            client, parser, _meteringRepo, _portfolioRepo, processRepo, signupRepo,
            NullOnboardingService.Instance, new TestClock(), _messageLog,
            NullLogger<QueuePollerService>.Instance);

        client.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-dup", "RSM-012", null, LoadSingleDayFixture()));

        // Process first time
        await poller.PollQueueAsync(QueueName.Timeseries, CancellationToken.None);

        // Enqueue again with same message ID
        client.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-dup", "RSM-012", null, LoadSingleDayFixture()));

        // Process second time — should skip
        var processed = await poller.PollQueueAsync(QueueName.Timeseries, CancellationToken.None);
        processed.Should().BeTrue();

        // Message should still be dequeued (skip + dequeue)
        var peek = await client.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        peek.Should().BeNull();
    }

    [Fact]
    public async Task Malformed_message_is_dead_lettered()
    {
        var client = new FakeDataHubClient();
        var parser = new CimJsonParser();
        var processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        var signupRepo = new SignupRepository(TestDatabase.ConnectionString);
        var poller = new QueuePollerService(
            client, parser, _meteringRepo, _portfolioRepo, processRepo, signupRepo,
            NullOnboardingService.Instance, new TestClock(), _messageLog,
            NullLogger<QueuePollerService>.Instance);

        client.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-bad", "RSM-012", null, "{ invalid json payload }"));

        var processed = await poller.PollQueueAsync(QueueName.Timeseries, CancellationToken.None);

        processed.Should().BeTrue();

        // Queue should be freed
        var peek = await client.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        peek.Should().BeNull();
    }

    [Fact]
    public async Task RSM007_creates_customer_and_activates_portfolio()
    {
        // This test verifies the complete RSM-022 activation flow:
        // 1. Signup created with customer info (customer_id = NULL)
        // 2. Process advances to effectuation_pending
        // 3. RSM-022 received (via QueuePoller)
        // 4. Customer created, Contract created, Process marked "completed"

        var ct = CancellationToken.None;
        const string gsrn = "571313100000012345"; // Must match RSM-022 fixture
        const string cprCvr = "9999999999"; // Unique CPR/CVR for this test

        // ──── ARRANGE ────

        // 1. Set up repositories and services
        var client = new FakeDataHubClient();
        var parser = new CimJsonParser();
        var processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        var signupRepo = new SignupRepository(TestDatabase.ConnectionString);
        // Set clock to early December to allow 15 business days notice for Jan 1 effective date (accounting for Christmas holidays)
        var clock = new TestClock { Today = new DateOnly(2024, 12, 5) };
        var onboardingService = new OnboardingService(
            signupRepo, _portfolioRepo, processRepo,
            new StubAddressLookupClient(), client, new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(), clock,
            NullLogger<OnboardingService>.Instance);

        var poller = new QueuePollerService(
            client, parser, _meteringRepo, _portfolioRepo, processRepo, signupRepo,
            onboardingService, clock, _messageLog,
            NullLogger<QueuePollerService>.Instance);

        // 2. Ensure grid area exists (required for RSM-022)
        await _portfolioRepo.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);

        // 3. Create product
        var product = await _portfolioRepo.CreateProductAsync(
            "Spot Test", "spot", 0.04m, null, 39.00m, ct);

        // 4. Create signup via OnboardingService
        var signupRequest = new SignupRequest(
            DarId: "test-dar-rsm022",
            CustomerName: "RSM-022 Test Customer",
            CprCvr: cprCvr,
            ContactType: "person",
            Email: "rsm022@test.com",
            Phone: "+4512345678",
            ProductId: product.Id,
            Type: "switch",
            EffectiveDate: new DateOnly(2025, 1, 1),
            Gsrn: gsrn
        );

        var signupResponse = await onboardingService.CreateSignupAsync(signupRequest, ct);
        var signup = await signupRepo.GetBySignupNumberAsync(signupResponse.SignupId, ct);

        // Verify initial state: customer_id is NULL
        signup.Should().NotBeNull();
        signup!.CustomerId.Should().BeNull("customer should not exist before RSM-022");

        // 5. Advance process to effectuation_pending
        var stateMachine = new ProcessStateMachine(processRepo, clock);
        await stateMachine.MarkSentAsync(signup.ProcessRequestId!.Value, "corr-rsm022-test", ct);
        await stateMachine.MarkAcknowledgedAsync(signup.ProcessRequestId.Value, ct);

        var processBefore = await processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
        processBefore!.Status.Should().Be("effectuation_pending");

        // Verify no customer exists for this CPR/CVR yet
        var customerBefore = await _portfolioRepo.GetCustomerByCprCvrAsync(cprCvr, ct);
        customerBefore.Should().BeNull("customer should not exist for this CPR/CVR before RSM-022");

        // ──── ACT ────

        // 6. Enqueue RSM-022 message and process it
        client.Enqueue(QueueName.MasterData,
            new DataHubMessage("msg-rsm022-test", "RSM-022", "corr-rsm022-test", LoadRsm022Fixture()));

        var processed = await poller.PollQueueAsync(QueueName.MasterData, ct);

        // ──── ASSERT ────

        processed.Should().BeTrue("RSM-022 should be processed successfully");

        // 7. Verify process marked as "completed"
        var processAfter = await processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
        processAfter!.Status.Should().Be("completed", "RSM-022 should mark process as completed");

        // 8. Verify Customer created for this CPR/CVR
        var customer = await _portfolioRepo.GetCustomerByCprCvrAsync(cprCvr, ct);
        customer.Should().NotBeNull("RSM-022 should create customer");
        customer!.Name.Should().Be("RSM-022 Test Customer");
        customer.CprCvr.Should().Be(cprCvr);
        customer.ContactType.Should().Be("private", "signup contact_type 'person' maps to customer contact_type 'private'");

        // 9. Verify Signup updated with customer_id and status "active"
        var signupAfter = await signupRepo.GetByIdAsync(signup.Id, ct);
        signupAfter.Should().NotBeNull();
        signupAfter!.CustomerId.Should().Be(customer.Id, "signup should be linked to customer");
        signupAfter.Status.Should().Be("active", "signup should be active after RSM-022");

        // 10. Verify Contract created
        var contracts = await _portfolioRepo.GetContractsForCustomerAsync(customer.Id, ct);
        contracts.Should().HaveCount(1, "RSM-022 should create contract");
        var contract = contracts[0];
        contract.Gsrn.Should().Be(gsrn);
        contract.ProductId.Should().Be(product.Id);
        contract.BillingFrequency.Should().Be("quarterly");
        contract.PaymentModel.Should().Be("aconto");

        // 11. Verify SupplyPeriod created
        var supplyPeriods = await _portfolioRepo.GetSupplyPeriodsAsync(gsrn, ct);
        supplyPeriods.Should().HaveCount(1, "RSM-022 should create supply period");
        var supplyPeriod = supplyPeriods[0];
        supplyPeriod.StartDate.Should().Be(new DateOnly(2025, 1, 1));
        supplyPeriod.EndDate.Should().BeNull("supply period should be open-ended");

        // 12. Verify message dequeued
        var peek = await client.PeekAsync(QueueName.MasterData, ct);
        peek.Should().BeNull("message should be dequeued after processing");
    }

    /// <summary>
    /// Stub address lookup that returns the GSRN used in RSM-022 fixture
    /// </summary>
    private sealed class StubAddressLookupClient : IAddressLookupClient
    {
        public Task<AddressLookupResult> LookupByDarIdAsync(string darId, CancellationToken ct)
        {
            return Task.FromResult(new AddressLookupResult(new List<MeteringPointInfo>
            {
                new("571313100000012345", "E17", "344")
            }));
        }
    }
}
