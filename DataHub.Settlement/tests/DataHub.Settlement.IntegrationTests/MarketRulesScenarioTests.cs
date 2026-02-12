using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Domain.MasterData;
using DataHub.Settlement.Infrastructure.Dashboard;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Parsing;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.Infrastructure.Settlement;
using DataHub.Settlement.Infrastructure.Tariff;
using DataHub.Settlement.Simulator;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// End-to-end scenario tests combining the HTTP DataHub Simulator with the real database.
/// Each test sends BRS requests through the simulator, processes queue messages,
/// stores data in TimescaleDB, and validates market rules enforcement.
/// </summary>
[Collection("Database")]
public sealed class MarketRulesScenarioTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Gsrn = "571313100000080001";
    private static readonly string Conn = TestDatabase.ConnectionString;

    private readonly HttpDataHubClient _datahub;
    private readonly HttpClient _admin;
    private readonly PortfolioRepository _portfolio;
    private readonly TariffRepository _tariffRepo;
    private readonly SpotPriceRepository _spotPriceRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly ProcessRepository _processRepo;
    private readonly ProcessStateMachine _stateMachine;
    private readonly BrsRequestBuilder _brsBuilder;

    static MarketRulesScenarioTests()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public MarketRulesScenarioTests(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        _datahub = new HttpDataHubClient(client);
        _admin = factory.CreateClient();

        _portfolio = new PortfolioRepository(Conn);
        _tariffRepo = new TariffRepository(Conn);
        _spotPriceRepo = new SpotPriceRepository(Conn);
        _meteringRepo = new MeteringDataRepository(Conn);
        _processRepo = new ProcessRepository(Conn);
        _stateMachine = new ProcessStateMachine(_processRepo, new TestClock());
        _brsBuilder = new BrsRequestBuilder();
    }

    // ── Scenario 1: Full onboarding through simulator + DB ───────────

    [Fact]
    public async Task Onboarding_through_simulator_accepts_and_creates_portfolio()
    {
        var ct = CancellationToken.None;
        const string gsrn = "571313100000080001";
        await ResetSimulator();
        await SeedReferenceData(ct);

        // Create customer, product, metering point, contract in DB
        var customer = await _portfolio.CreateCustomerAsync("Scenario Kunde", "0101901234", "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot Scenario", "spot", 4.0m, null, 39.00m, ct);
        var mp = new MeteringPoint(gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _portfolio.CreateMeteringPointAsync(mp, ct);
        await _portfolio.CreateContractAsync(customer.Id, gsrn, product.Id, "monthly", "post_payment", new DateOnly(2025, 1, 1), ct);

        // Send BRS-001 through HTTP simulator
        var payload = _brsBuilder.BuildBrs001(gsrn, "0101901234", new DateOnly(2025, 1, 1));
        var response = await _datahub.SendRequestAsync("supplier_switch", payload, ct);

        response.Accepted.Should().BeTrue();
        response.CorrelationId.Should().NotBeNullOrEmpty();

        // Create process in DB and advance state machine
        var process = await _stateMachine.CreateRequestAsync(gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await _stateMachine.MarkSentAsync(process.Id, response.CorrelationId, ct);
        await _stateMachine.MarkAcknowledgedAsync(process.Id, ct);

        // Activate metering point + create supply period
        await _portfolio.ActivateMeteringPointAsync(gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        await _portfolio.CreateSupplyPeriodAsync(gsrn, new DateOnly(2025, 1, 1), ct);
        await _stateMachine.MarkCompletedAsync(process.Id, ct);

        // Verify DB state
        var dbProcess = await _processRepo.GetAsync(process.Id, ct);
        dbProcess!.Status.Should().Be("completed");

        var summary = await new SimulationService(Conn).GetMeteringPointSummaryAsync(gsrn, ct);
        summary.Should().NotBeNull();
        summary!.ConnectionStatus.Should().Be("connected");
        summary.ActivatedAt.Should().NotBeNull();
        summary.SupplyPeriods.Should().ContainSingle(sp => sp.EndDate == null);
    }

    // ── Scenario 2: Duplicate BRS-001 rejected by simulator ──────────

    [Fact]
    public async Task Duplicate_brs001_rejected_by_simulator_with_E16()
    {
        var ct = CancellationToken.None;
        const string gsrn = "571313100000080002";
        await ResetSimulator();

        // First BRS-001 — accepted
        var payload = _brsBuilder.BuildBrs001(gsrn, "0101901234", new DateOnly(2025, 1, 1));
        var first = await _datahub.SendRequestAsync("supplier_switch", payload, ct);
        first.Accepted.Should().BeTrue();

        // Second BRS-001 for same GSRN — rejected by simulator
        var second = await _datahub.SendRequestAsync("supplier_switch", payload, ct);
        second.Accepted.Should().BeFalse();
        second.RejectionReason.Should().Be("E16");
    }

    // ── Scenario 3: Onboard, settle, then duplicate blocked by both ──

    [Fact]
    public async Task Full_onboard_then_duplicate_rejected_by_simulator_and_market_rules()
    {
        var ct = CancellationToken.None;
        const string gsrn = "571313100000080003";
        await ResetSimulator();
        await SeedReferenceData(ct);

        // Full onboard in DB
        await OnboardGsrn(gsrn, ct);

        // Simulator also knows this GSRN is active (from first BRS-001)
        // Try duplicate BRS-001 through simulator
        var payload = _brsBuilder.BuildBrs001(gsrn, "0101901234", new DateOnly(2025, 1, 1));
        var simResponse = await _datahub.SendRequestAsync("supplier_switch", payload, ct);
        simResponse.Accepted.Should().BeFalse("simulator should reject duplicate GSRN");

        // Also verify DB-level market rules reject it
        var dbCheck = await MarketRules.CanChangeSupplierAsync(gsrn, Conn, ct);
        dbCheck.IsValid.Should().BeFalse("DB market rules should also reject — active supply exists");
        dbCheck.ErrorMessage.Should().Contain("Already supplying");
    }

    // ── Scenario 4: End of supply for unknown GSRN rejected ──────────

    [Fact]
    public async Task End_of_supply_for_inactive_gsrn_rejected_by_simulator()
    {
        var ct = CancellationToken.None;
        const string gsrn = "571313100000080004";
        await ResetSimulator();

        var payload = _brsBuilder.BuildBrs002(gsrn, new DateOnly(2025, 2, 1));
        var response = await _datahub.SendRequestAsync("end_of_supply", payload, ct);

        response.Accepted.Should().BeFalse();
        response.RejectionReason.Should().Be("E16");
    }

    // ── Scenario 5: Full lifecycle — onboard → metering → settle → offboard ──

    [Fact]
    public async Task Full_lifecycle_through_simulator_and_database()
    {
        var ct = CancellationToken.None;
        const string gsrn = "571313100000080005";
        await ResetSimulator();
        await SeedReferenceData(ct);

        // ── Phase 1: Onboard via simulator + DB ──
        var customer = await _portfolio.CreateCustomerAsync("Lifecycle Kunde", "0101901234", "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot Lifecycle", "spot", 4.0m, null, 39.00m, ct);
        var mp = new MeteringPoint(gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _portfolio.CreateMeteringPointAsync(mp, ct);
        await _portfolio.CreateContractAsync(customer.Id, gsrn, product.Id, "monthly", "post_payment", new DateOnly(2025, 1, 1), ct);

        var brs001Payload = _brsBuilder.BuildBrs001(gsrn, "0101901234", new DateOnly(2025, 1, 1));
        var brs001Response = await _datahub.SendRequestAsync("supplier_switch", brs001Payload, ct);
        brs001Response.Accepted.Should().BeTrue();

        var process = await _stateMachine.CreateRequestAsync(gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await _stateMachine.MarkSentAsync(process.Id, brs001Response.CorrelationId, ct);
        await _stateMachine.MarkAcknowledgedAsync(process.Id, ct);
        await _portfolio.ActivateMeteringPointAsync(gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        await _portfolio.CreateSupplyPeriodAsync(gsrn, new DateOnly(2025, 1, 1), ct);
        await _stateMachine.MarkCompletedAsync(process.Id, ct);

        // ── Phase 2: Store metering data ──
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 744).Select(i =>
            new MeteringDataRow(start.AddHours(i), "PT1H", 0.55m, "A01", "msg-lifecycle")).ToList();
        await _meteringRepo.StoreTimeSeriesAsync(gsrn, rows, ct);

        var consumption = await _meteringRepo.GetConsumptionAsync(gsrn, start, start.AddMonths(1), ct);
        consumption.Should().HaveCount(744);

        // ── Phase 3: Run settlement ──
        var settlementCheck = await MarketRules.CanRunSettlementAsync(gsrn, Conn, ct);
        settlementCheck.IsValid.Should().BeTrue();

        var spotPrices = await _spotPriceRepo.GetPricesAsync("DK1", start, start.AddMonths(1), ct);
        var gridRates = await _tariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await _tariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct);
        var gridSub = await _tariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct);

        var engine = new SettlementEngine();
        var result = engine.Calculate(new SettlementRequest(
            gsrn, new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            consumption, spotPrices, gridRates, 0.054m, 0.049m, elTax, gridSub,
            product.MarginOrePerKwh / 100m, (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth));

        result.Total.Should().BeGreaterThan(0);
        result.TotalKwh.Should().Be(409.200m);

        // ── Phase 4: Offboard via simulator ──
        var offboardCheck = await MarketRules.CanOffboardAsync(gsrn, Conn, ct);
        offboardCheck.IsValid.Should().BeTrue();

        var brs002Payload = _brsBuilder.BuildBrs002(gsrn, new DateOnly(2025, 2, 1));
        var brs002Response = await _datahub.SendRequestAsync("end_of_supply", brs002Payload, ct);
        brs002Response.Accepted.Should().BeTrue();

        await _stateMachine.MarkOffboardingAsync(process.Id, ct);
        await _portfolio.EndSupplyPeriodAsync(gsrn, new DateOnly(2025, 2, 1), "supplier_switch", ct);
        await _portfolio.EndContractAsync(gsrn, new DateOnly(2025, 2, 1), ct);
        await _portfolio.DeactivateMeteringPointAsync(gsrn, new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        await _stateMachine.MarkFinalSettledAsync(process.Id, ct);

        // ── Phase 5: Verify everything is closed ──
        var afterOffboard = await _processRepo.GetAsync(process.Id, ct);
        afterOffboard!.Status.Should().Be("final_settled");

        // All follow-up operations should be rejected
        var meteringCheck = await MarketRules.CanReceiveMeteringAsync(gsrn, Conn, ct);
        meteringCheck.IsValid.Should().BeFalse();

        var settleCheck = await MarketRules.CanRunSettlementAsync(gsrn, Conn, ct);
        settleCheck.IsValid.Should().BeFalse();

        var acontoCheck = await MarketRules.CanBillAcontoAsync(gsrn, Conn, ct);
        acontoCheck.IsValid.Should().BeFalse();

        // Simulator also rejects duplicate BRS-001 (GSRN was deactivated by end_of_supply)
        // Re-onboarding should be possible in both DB and simulator
        var reOnboardCheck = await MarketRules.CanChangeSupplierAsync(gsrn, Conn, ct);
        reOnboardCheck.IsValid.Should().BeTrue("DB allows re-onboard after offboard");
    }

    // ── Scenario 6: Enqueue + queue processing + DB ─────────────────

    [Fact]
    public async Task Enqueue_rsm022_and_rsm012_processes_messages_into_database()
    {
        var ct = CancellationToken.None;
        const string gsrn = "571313100000080006";
        await ResetSimulator();

        // Build RSM-022 and RSM-012 payloads with our unique GSRN
        var rsm022 = BuildRsm022(gsrn);
        var rsm012 = BuildRsm012(gsrn, 744);

        // Enqueue messages via admin endpoint
        await EnqueueMessage("MasterData", "RSM-022", "corr-scenario-006", rsm022);
        await EnqueueMessage("Timeseries", "RSM-012", null, rsm012);

        // Peek the MasterData queue — should have RSM-022
        var msg = await _datahub.PeekAsync(QueueName.MasterData, ct);
        msg.Should().NotBeNull();
        msg!.MessageType.Should().Be("RSM-022");
        msg.RawPayload.Should().Contain(gsrn);

        // Parse RSM-022 and store in DB
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);
        await using (var conn = new NpgsqlConnection(Conn))
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync("""
                INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, grid_area_code, grid_operator_gln, price_area)
                VALUES (@Gsrn, 'E17', 'flex', '344', '5790000392261', 'DK1')
                ON CONFLICT (gsrn) DO NOTHING
                """, new { Gsrn = gsrn });
        }

        var parser = new CimJsonParser();
        var masterData = parser.ParseRsm022(msg.RawPayload);
        masterData.MeteringPointId.Should().Be(gsrn);

        await _portfolio.ActivateMeteringPointAsync(gsrn, masterData.SupplyStart.UtcDateTime, ct);
        await _datahub.DequeueAsync(msg.MessageId, ct);

        // Verify metering point is activated in DB
        var summary = await new SimulationService(Conn).GetMeteringPointSummaryAsync(gsrn, ct);
        summary.Should().NotBeNull();
        summary!.ActivatedAt.Should().NotBeNull();

        // Peek Timeseries queue — should have RSM-012
        var tsMsg = await _datahub.PeekAsync(QueueName.Timeseries, ct);
        tsMsg.Should().NotBeNull();
        tsMsg!.MessageType.Should().Be("RSM-012");

        // Process RSM-012 through the poller into DB
        var messageLog = new MessageLog(Conn);
        var meteringRepo = new MeteringDataRepository(Conn);
        var processRepo = new ProcessRepository(Conn);
        var signupRepo = new SignupRepository(Conn);
        var poller = new QueuePollerService(
            _datahub, parser, meteringRepo, _portfolio, processRepo, signupRepo,
            NullOnboardingService.Instance, new Infrastructure.Tariff.TariffRepository(Conn),
            new Infrastructure.DataHub.BrsRequestBuilder(), new NullMessageRepository(),
            new TestClock(), messageLog,
            new NullInvoiceService(),
            NullLogger<QueuePollerService>.Instance);
        await poller.PollQueueAsync(QueueName.Timeseries, ct);

        // Verify metering data landed in DB
        var consumption = await meteringRepo.GetConsumptionAsync(gsrn,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        consumption.Should().HaveCount(744);
        consumption.Sum(c => c.QuantityKwh).Should().BeGreaterThan(0);
    }

    // ── Scenario 7: Admin activate/deactivate endpoints ──────────────

    [Fact]
    public async Task Admin_activate_deactivate_controls_simulator_gsrn_state()
    {
        var ct = CancellationToken.None;
        const string gsrn = "571313100000080007";
        await ResetSimulator();

        // GSRN starts inactive — BRS-001 should be accepted
        var payload = _brsBuilder.BuildBrs001(gsrn, "0101901234", new DateOnly(2025, 1, 1));
        var first = await _datahub.SendRequestAsync("supplier_switch", payload, ct);
        first.Accepted.Should().BeTrue();

        // Now GSRN is active — deactivate via admin
        var deactivate = await _admin.PostAsync($"/admin/deactivate/{gsrn}", null);
        deactivate.EnsureSuccessStatusCode();

        // BRS-001 should be accepted again (GSRN is inactive)
        var second = await _datahub.SendRequestAsync("supplier_switch", payload, ct);
        second.Accepted.Should().BeTrue();

        // Manually activate via admin
        var activate = await _admin.PostAsync($"/admin/activate/{gsrn}", null);
        activate.EnsureSuccessStatusCode();

        // BRS-001 should be rejected (GSRN is active)
        var third = await _datahub.SendRequestAsync("supplier_switch", payload, ct);
        third.Accepted.Should().BeFalse();
        third.RejectionReason.Should().Be("E16");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task EnqueueMessage(string queue, string messageType, string? correlationId, string payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Queue = queue,
            MessageType = messageType,
            CorrelationId = correlationId,
            Payload = payload,
        });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _admin.PostAsync("/admin/enqueue", content);
        response.EnsureSuccessStatusCode();
    }

    private static string BuildRsm022(string gsrn) => $$"""
        {
          "MarketDocument": {
            "mRID": "msg-rsm022-{{Guid.NewGuid():N}}",
            "type": "E44",
            "MktActivityRecord": {
              "MarketEvaluationPoint": {
                "mRID": "{{gsrn}}",
                "type": "E17",
                "settlementMethod": "D01",
                "linkedMarketEvaluationPoint": { "mRID": "344" },
                "inDomain": { "mRID": "5790000392261" }
              },
              "Period": {
                "timeInterval": { "start": "2025-01-01T00:00:00Z" }
              }
            }
          }
        }
        """;

    private static string BuildRsm012(string gsrn, int hours)
    {
        var points = new System.Text.StringBuilder();
        for (var i = 1; i <= hours; i++)
        {
            var hour = (i - 1) % 24;
            var kwh = hour switch
            {
                >= 0 and <= 5 => "0.300",
                >= 6 and <= 15 => "0.500",
                >= 16 and <= 19 => "1.200",
                _ => "0.400",
            };
            if (i > 1) points.Append(',');
            points.Append($$"""{"position":{{i}},"quantity":{{kwh}},"quality":"A01"}""");
        }

        return $$"""
            {
              "MarketDocument": {
                "mRID": "msg-rsm012-{{Guid.NewGuid():N}}",
                "Series": [{
                  "mRID": "txn-{{Guid.NewGuid():N}}",
                  "MarketEvaluationPoint": { "mRID": "{{gsrn}}", "type": "E17" },
                  "Period": {
                    "resolution": "PT1H",
                    "timeInterval": {
                      "start": "2025-01-01T00:00:00Z",
                      "end": "2025-02-01T00:00:00Z"
                    },
                    "Point": [{{points}}]
                  }
                }]
              }
            }
            """;
    }

    private async Task ResetSimulator()
    {
        await _admin.PostAsync("/admin/reset", null);
    }

    private async Task SeedReferenceData(CancellationToken ct)
    {
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);

        var gridRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
        {
            >= 1 and <= 6 => 0.06m,
            >= 7 and <= 16 => 0.18m,
            >= 17 and <= 20 => 0.54m,
            _ => 0.06m,
        })).ToList();
        await _tariffRepo.SeedGridTariffAsync("344", "grid", new DateOnly(2025, 1, 1), gridRates, ct);
        await _tariffRepo.SeedSubscriptionAsync("344", "grid", 49.00m, new DateOnly(2025, 1, 1), ct);
        await _tariffRepo.SeedElectricityTaxAsync(0.008m, new DateOnly(2025, 1, 1), ct);

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

    private async Task OnboardGsrn(string gsrn, CancellationToken ct)
    {
        var customer = await _portfolio.CreateCustomerAsync($"Customer-{gsrn[^4..]}", "0101901234", "private", null, ct);
        var product = await _portfolio.CreateProductAsync($"Spot-{gsrn[^4..]}", "spot", 4.0m, null, 39.00m, ct);
        var mp = new MeteringPoint(gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _portfolio.CreateMeteringPointAsync(mp, ct);
        await _portfolio.CreateContractAsync(customer.Id, gsrn, product.Id, "monthly", "post_payment", new DateOnly(2025, 1, 1), ct);

        // Send through simulator
        var payload = _brsBuilder.BuildBrs001(gsrn, "0101901234", new DateOnly(2025, 1, 1));
        var response = await _datahub.SendRequestAsync("supplier_switch", payload, ct);
        response.Accepted.Should().BeTrue();

        // Advance DB state
        var process = await _stateMachine.CreateRequestAsync(gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await _stateMachine.MarkSentAsync(process.Id, response.CorrelationId, ct);
        await _stateMachine.MarkAcknowledgedAsync(process.Id, ct);
        await _portfolio.ActivateMeteringPointAsync(gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        await _portfolio.CreateSupplyPeriodAsync(gsrn, new DateOnly(2025, 1, 1), ct);
        await _stateMachine.MarkCompletedAsync(process.Id, ct);
    }
}
