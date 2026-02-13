using Dapper;
using DataHub.Settlement.Application.AddressLookup;
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
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// End-to-end test: retroactive move-in (BRS-009) → metering data → settlement orchestration → DB store.
/// Covers two audit gaps: BRS-009 (move-in) path and SettlementOrchestrationService → SettlementResultStore.
/// </summary>
[Collection("Database")]
public class MoveInSettlementTests
{
    private const string Gsrn = "571313100000054321";
    private const string CprCvr = "1234567890";

    private readonly PortfolioRepository _portfolio;
    private readonly TariffRepository _tariffRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly SpotPriceRepository _spotPriceRepo;
    private readonly ProcessRepository _processRepo;

    public MoveInSettlementTests(TestDatabase db)
    {
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
        _tariffRepo = new TariffRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _spotPriceRepo = new SpotPriceRepository(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
    }

    [Fact]
    public async Task Move_in_back_in_time_receives_metering_and_produces_settlement()
    {
        var ct = CancellationToken.None;

        // The effective date is 7 days ago (max retroactive for BRS-009 move-in)
        var today = new DateOnly(2025, 2, 15);
        var effectiveDate = today.AddDays(-7); // 2025-02-08
        var settlementPeriodEnd = effectiveDate.AddMonths(1); // 2025-03-08 (orchestration uses effective_date + 1 month)
        var clock = new TestClock { Today = today };

        // ──── 0. CLEANUP ────
        await using (var conn = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync("DELETE FROM settlement.settlement_line WHERE metering_point_id = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM portfolio.contract WHERE gsrn = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM portfolio.supply_period WHERE gsrn = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM portfolio.signup WHERE gsrn = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM lifecycle.process_event WHERE process_request_id IN (SELECT id FROM lifecycle.process_request WHERE gsrn = @Gsrn)", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM lifecycle.process_request WHERE gsrn = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM metering.metering_data WHERE metering_point_id = @Gsrn", new { Gsrn });
        }

        // ──── 1. SEED: tariffs, spot prices, grid area ────
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);
        await SeedTariffsAsync(ct);
        await SeedSpotPricesAsync(effectiveDate, settlementPeriodEnd, ct);

        // ──── 2. ARRANGE: create customer, product, metering point, contract ────
        var customer = await _portfolio.CreateCustomerAsync("Move-In Test Kunde", CprCvr, "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot MoveIn", "spot", 4.0m, null, 39.00m, ct);

        var mp = new MeteringPoint(Gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        try { await _portfolio.CreateMeteringPointAsync(mp, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.PostgresException) { }

        try { await _portfolio.CreateContractAsync(customer.Id, Gsrn, product.Id, "monthly", "post_payment", effectiveDate, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.PostgresException) { }

        // ──── 3. CREATE PROCESS: move_in with retroactive effective date ────
        var stateMachine = new ProcessStateMachine(_processRepo, clock);
        var process = await stateMachine.CreateRequestAsync(Gsrn, "move_in", effectiveDate, ct);

        // ──── 4. ADVANCE PROCESS: pending → sent → acknowledged → effectuation_pending → completed ────
        await stateMachine.MarkSentAsync(process.Id, "corr-movein-test", ct);
        await stateMachine.MarkAcknowledgedAsync(process.Id, ct);

        var processBefore = await _processRepo.GetAsync(process.Id, ct);
        processBefore!.Status.Should().Be("effectuation_pending");

        await stateMachine.MarkCompletedAsync(process.Id, ct);

        var processAfter = await _processRepo.GetAsync(process.Id, ct);
        processAfter!.Status.Should().Be("completed");

        // ──── 5. ACTIVATE metering point and create supply period ────
        await _portfolio.ActivateMeteringPointAsync(Gsrn, effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct);

        try { await _portfolio.CreateSupplyPeriodAsync(Gsrn, effectiveDate, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.PostgresException) { }

        // ──── 6. STORE METERING DATA: retroactive backfill from effective date through period end ────
        var rows = new List<MeteringDataRow>();
        var start = effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = settlementPeriodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var totalHours = (int)(end - start).TotalHours;

        for (var i = 0; i < totalHours; i++)
        {
            var ts = start.AddHours(i);
            // Varying consumption pattern
            var kwh = ts.Hour switch
            {
                >= 0 and <= 5 => 0.200m,
                >= 6 and <= 15 => 0.500m,
                >= 16 and <= 20 => 0.800m,
                _ => 0.300m,
            };
            rows.Add(new MeteringDataRow(ts, "PT1H", kwh, "A03", "test-movein"));
        }

        await _meteringRepo.StoreTimeSeriesAsync(Gsrn, rows, ct);

        // Verify metering data stored
        var consumption = await _meteringRepo.GetConsumptionAsync(Gsrn, start, end, ct);
        consumption.Should().HaveCount(totalHours);

        // ──── 7. RUN SETTLEMENT ORCHESTRATION ────
        var completenessChecker = new MeteringCompletenessChecker(TestDatabase.ConnectionString);
        var dataLoader = new SettlementDataLoader(_meteringRepo, _spotPriceRepo, _tariffRepo);
        var engine = new SettlementEngine();
        var resultStore = new SettlementResultStore(TestDatabase.ConnectionString);

        var orchestration = new SettlementOrchestrationService(
            _processRepo, _portfolio,
            completenessChecker, dataLoader, engine, resultStore,
            NullLogger<SettlementOrchestrationService>.Instance);

        await orchestration.RunTickAsync(ct);

        // ──── 8. ASSERT: settlement_run row exists ────
        await using var assertConn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await assertConn.OpenAsync(ct);

        var runRow = await assertConn.QuerySingleAsync<dynamic>(
            "SELECT id, metering_point_id, status FROM settlement.settlement_run WHERE metering_point_id = @Gsrn",
            new { Gsrn });

        ((string)runRow.metering_point_id).Should().Be(Gsrn);
        ((string)runRow.status).Should().Be("completed");

        // ──── 9. ASSERT: settlement_line rows exist with correct charge types ────
        var lineRows = (await assertConn.QueryAsync<dynamic>(
            "SELECT charge_type, total_amount, vat_amount FROM settlement.settlement_line WHERE settlement_run_id = @RunId",
            new { RunId = (Guid)runRow.id })).ToList();

        lineRows.Should().NotBeEmpty("settlement lines should be created");
        var chargeTypes = lineRows.Select(l => (string)l.charge_type).ToList();
        chargeTypes.Should().Contain("energy");
        chargeTypes.Should().Contain("grid_tariff");
        chargeTypes.Should().Contain("supplier_subscription");

        // All amounts should be positive
        foreach (var line in lineRows)
            ((decimal)line.total_amount).Should().BeGreaterThan(0, $"charge type '{line.charge_type}' should have positive amount");

        // ──── 10. ASSERT: billing_period row covers the correct period ────
        var billingPeriod = await assertConn.QuerySingleAsync<dynamic>(
            """
            SELECT bp.period_start, bp.period_end
            FROM settlement.billing_period bp
            JOIN settlement.settlement_run sr ON sr.billing_period_id = bp.id
            WHERE sr.metering_point_id = @Gsrn
            """,
            new { Gsrn });

        DateOnly.FromDateTime((DateTime)billingPeriod.period_start).Should().Be(effectiveDate);
        DateOnly.FromDateTime((DateTime)billingPeriod.period_end).Should().Be(effectiveDate.AddMonths(1));
    }

    private async Task SeedTariffsAsync(CancellationToken ct)
    {
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
    }

    private async Task SeedSpotPricesAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var prices = new List<SpotPriceRow>();
        var start = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hours = (int)(end - start).TotalHours;

        for (var i = 0; i < hours; i++)
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
