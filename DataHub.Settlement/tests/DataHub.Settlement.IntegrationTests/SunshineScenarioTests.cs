using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Parsing;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.Infrastructure.Settlement;
using DataHub.Settlement.Infrastructure.Tariff;
using DataHub.Settlement.Infrastructure;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// End-to-end sunshine scenario: CRM → portfolio → BRS-001 → RSM-022 → RSM-012 → settlement → golden master.
/// Uses FakeDataHubClient (no HTTP, no external dependencies beyond TimescaleDB).
/// </summary>
[Collection("Database")]
public class SunshineScenarioTests
{
    private const string Gsrn = "571313100000012345";

    private readonly PortfolioRepository _portfolio;
    private readonly TariffRepository _tariffRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly SpotPriceRepository _spotPriceRepo;
    private readonly MessageLog _messageLog;
    private readonly ProcessRepository _processRepo;

    public SunshineScenarioTests(TestDatabase db)
    {
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
        _tariffRepo = new TariffRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _spotPriceRepo = new SpotPriceRepository(TestDatabase.ConnectionString);
        _messageLog = new MessageLog(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
    }

    [Fact]
    public async Task Full_sunshine_scenario_produces_golden_master_result()
    {
        var ct = CancellationToken.None;

        // ──── 0. CLEANUP: remove leftover state from prior tests sharing this GSRN ────
        await using (var cleanConn = new Npgsql.NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await cleanConn.OpenAsync(ct);
            await cleanConn.ExecuteAsync("DELETE FROM settlement.settlement_line WHERE metering_point_id = @Gsrn", new { Gsrn });
            await cleanConn.ExecuteAsync("DELETE FROM portfolio.contract WHERE gsrn = @Gsrn", new { Gsrn });
            await cleanConn.ExecuteAsync("DELETE FROM portfolio.supply_period WHERE gsrn = @Gsrn", new { Gsrn });
            await cleanConn.ExecuteAsync("DELETE FROM portfolio.signup WHERE gsrn = @Gsrn", new { Gsrn });
            await cleanConn.ExecuteAsync("DELETE FROM lifecycle.process_event WHERE process_request_id IN (SELECT id FROM lifecycle.process_request WHERE gsrn = @Gsrn)", new { Gsrn });
            await cleanConn.ExecuteAsync("DELETE FROM lifecycle.process_request WHERE gsrn = @Gsrn", new { Gsrn });
        }

        // ──── 1. SEED: tariffs, spot prices, electricity tax ────
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);
        await SeedTariffsAsync(ct);
        await SeedSpotPricesAsync(ct);

        // ──── 2. ARRANGE: create customer + product + contract via CRM ────
        var customer = await _portfolio.CreateCustomerAsync("Test Kunde", "0101901234", "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot Standard", "spot", 4.0m, null, 39.00m, ct);

        var mp = new MeteringPoint(Gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        try
        {
            await _portfolio.CreateMeteringPointAsync(mp, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.PostgresException)
        {
            // Metering point may already exist from another test in the same collection (e.g., QueuePollerTests)
        }

        try
        {
            await _portfolio.CreateContractAsync(
                customer.Id, Gsrn, product.Id, "monthly", "post_payment", new DateOnly(2025, 1, 1), ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.PostgresException)
        {
            // Contract may already exist from another test (e.g., QueuePollerTests)
            // This test focuses on settlement calculation, not contract management
        }

        // ──── 3. ACT: submit BRS-001 ────
        var stateMachine = new ProcessStateMachine(_processRepo, new TestClock());
        var processRequest = await stateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);

        var fakeClient = new FakeDataHubClient();
        var brsBuilder = new Infrastructure.DataHub.BrsRequestBuilder();
        var cimPayload = brsBuilder.BuildBrs001(Gsrn, "0101901234", new DateOnly(2025, 1, 1));
        var response = await fakeClient.SendRequestAsync("BRS-001", cimPayload, ct);

        response.Accepted.Should().BeTrue();
        await stateMachine.MarkSentAsync(processRequest.Id, response.CorrelationId, ct);

        // ──── 4. ASSERT: process state = sent_to_datahub ────
        var afterSent = await _processRepo.GetAsync(processRequest.Id, ct);
        afterSent!.Status.Should().Be("sent_to_datahub");

        // ──── 5. ACT: RSM-001 (acknowledged by FakeDataHubClient synchronously) ────
        await stateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);
        var afterAck = await _processRepo.GetAsync(processRequest.Id, ct);
        afterAck!.Status.Should().Be("effectuation_pending");

        // ──── 6. ACT: poll MasterData queue → RSM-022 → activate metering point ────
        var rsm022Json = File.ReadAllText(Path.Combine("..", "..", "..", "..", "..", "fixtures", "rsm022-activation.json"));
        fakeClient.Enqueue(QueueName.MasterData, new DataHubMessage("msg-rsm022", "RSM-022", null, rsm022Json));

        var parser = new CimJsonParser();
        var masterData = parser.ParseRsm022(rsm022Json);
        await _portfolio.ActivateMeteringPointAsync(Gsrn, masterData.SupplyStart.UtcDateTime, ct);

        try
        {
            await _portfolio.CreateSupplyPeriodAsync(Gsrn, DateOnly.FromDateTime(masterData.SupplyStart.UtcDateTime), ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.PostgresException)
        {
            // Supply period may already exist from another test (e.g., QueuePollerTests)
        }

        await fakeClient.DequeueAsync("msg-rsm022", ct);

        // ──── 7. ACT: complete process ────
        await stateMachine.MarkCompletedAsync(processRequest.Id, ct);
        var afterComplete = await _processRepo.GetAsync(processRequest.Id, ct);
        afterComplete!.Status.Should().Be("completed");

        // ──── 8. ACT: poll Timeseries queue → 31 × RSM-012 → store metering data ────
        var rsm012Json = File.ReadAllText(Path.Combine("..", "..", "..", "..", "..", "fixtures", "rsm012-multi-day.json"));
        fakeClient.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-rsm012", "RSM-012", null, rsm012Json));

        var nullEffectuation = new EffectuationService(
            TestDatabase.ConnectionString, NullOnboardingService.Instance, new NullInvoiceService(),
            fakeClient, new Infrastructure.DataHub.BrsRequestBuilder(), new NullMessageRepository(),
            new TestClock(), NullLogger<EffectuationService>.Instance);
        var poller = new QueuePollerService(
            fakeClient, parser, _meteringRepo, _portfolio, _processRepo, new SignupRepository(TestDatabase.ConnectionString),
            NullOnboardingService.Instance, new Infrastructure.Tariff.TariffRepository(TestDatabase.ConnectionString),
            new Infrastructure.DataHub.BrsRequestBuilder(), new NullMessageRepository(),
            new TestClock(), _messageLog,
            new NullInvoiceService(),
            nullEffectuation,
            NullLogger<QueuePollerService>.Instance);
        await poller.PollQueueAsync(QueueName.Timeseries, ct);

        // ──── 9. ASSERT: 744 rows stored ────
        var consumption = await _meteringRepo.GetConsumptionAsync(Gsrn,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        consumption.Should().HaveCount(744);

        // ──── 10. ACT: run settlement engine ────
        var spotPrices = await _spotPriceRepo.GetPricesAsync("DK1",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        var gridRates = await _tariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var systemRate = 0.054m; // Would come from tariff DB in full implementation
        var transmissionRate = 0.049m;
        var electricityTaxRate = await _tariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct) ?? 0m;
        var gridSubscription = await _tariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct) ?? 0m;

        var engine = new SettlementEngine();
        var settlementRequest = new SettlementRequest(
            Gsrn,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            consumption, spotPrices,
            gridRates, systemRate, transmissionRate, electricityTaxRate,
            gridSubscription,
            product.MarginOrePerKwh / 100m, // ore → DKK
            (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth);

        var result = engine.Calculate(settlementRequest);

        // ──── 11. ASSERT: golden master #1 amounts ────
        result.TotalKwh.Should().Be(409.200m);
        result.Lines.Single(l => l.ChargeType == "energy").Amount.Should().Be(386.51m);
        result.Lines.Single(l => l.ChargeType == "grid_tariff").Amount.Should().Be(114.58m);
        result.Lines.Single(l => l.ChargeType == "system_tariff").Amount.Should().Be(22.10m);
        result.Lines.Single(l => l.ChargeType == "transmission_tariff").Amount.Should().Be(20.05m);
        result.Lines.Single(l => l.ChargeType == "electricity_tax").Amount.Should().Be(3.27m);
        result.Lines.Single(l => l.ChargeType == "grid_subscription").Amount.Should().Be(49.00m);
        result.Lines.Single(l => l.ChargeType == "supplier_subscription").Amount.Should().Be(39.00m);
        result.Subtotal.Should().Be(634.51m);
        result.VatAmount.Should().Be(158.63m);
        result.Total.Should().Be(793.14m);
    }

    private async Task SeedTariffsAsync(CancellationToken ct)
    {
        // Grid tariff (time-differentiated)
        var gridRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
        {
            >= 1 and <= 6 => 0.06m,
            >= 7 and <= 16 => 0.18m,
            >= 17 and <= 20 => 0.54m,
            _ => 0.06m,
        })).ToList();
        await _tariffRepo.SeedGridTariffAsync("344", "grid", new DateOnly(2025, 1, 1), gridRates, ct);

        // Grid subscription
        await _tariffRepo.SeedSubscriptionAsync("344", "grid", 49.00m, new DateOnly(2025, 1, 1), ct);

        // Electricity tax
        await _tariffRepo.SeedElectricityTaxAsync(0.008m, new DateOnly(2025, 1, 1), ct);
    }

    private async Task SeedSpotPricesAsync(CancellationToken ct)
    {
        var prices = new List<SpotPriceRow>();
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 744; i++)
        {
            var hour = start.AddHours(i);
            var price = hour.Hour switch
            {
                >= 0 and <= 5 => 45m,
                >= 6 and <= 15 => 85m,
                >= 16 and <= 19 => 125m,
                _ => 55m,
            };
            prices.Add(new SpotPriceRow("DK1", hour, price));
        }
        await _spotPriceRepo.StorePricesAsync(prices, ct);
    }
}
