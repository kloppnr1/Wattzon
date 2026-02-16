using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Billing;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
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
/// Tests the correction workflow: original settlement → revised metering data → correction delta.
/// </summary>
[Collection("Database")]
public class CorrectionWorkflowTests
{
    private const string Gsrn = "571313100000098765";

    private readonly PortfolioRepository _portfolio;
    private readonly TariffRepository _tariffRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly SpotPriceRepository _spotPriceRepo;
    private readonly ProcessRepository _processRepo;
    private readonly BillingRepository _billingRepo;

    public CorrectionWorkflowTests(TestDatabase db)
    {
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
        _tariffRepo = new TariffRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _spotPriceRepo = new SpotPriceRepository(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        _billingRepo = new BillingRepository(TestDatabase.ConnectionString);
    }

    [Fact]
    public async Task Revised_metering_data_triggers_correction_settlement()
    {
        var ct = CancellationToken.None;
        var periodStart = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 2, 1);
        var clock = new TestClock { Today = new DateOnly(2025, 2, 15) };

        // ──── 0. CLEANUP ────
        await using (var conn = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync("DELETE FROM settlement.correction_settlement WHERE metering_point_id = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM settlement.settlement_line WHERE metering_point_id = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn });
            // Also clean up any stale runs for the same billing period + grid area to avoid unique constraint violations
            // Must delete invoice_lines first since they may reference settlement_lines via FK
            await conn.ExecuteAsync("""
                DELETE FROM billing.invoice_line WHERE settlement_line_id IN (
                    SELECT sl.id FROM settlement.settlement_line sl
                    JOIN settlement.settlement_run sr ON sl.settlement_run_id = sr.id
                    JOIN settlement.billing_period bp ON sr.billing_period_id = bp.id
                    WHERE bp.period_start = @PeriodStart AND bp.period_end = @PeriodEnd AND sr.grid_area_code = '344')
                """, new { PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 2, 1) });
            await conn.ExecuteAsync("""
                DELETE FROM billing.invoice WHERE settlement_run_id IN (
                    SELECT sr.id FROM settlement.settlement_run sr
                    JOIN settlement.billing_period bp ON sr.billing_period_id = bp.id
                    WHERE bp.period_start = @PeriodStart AND bp.period_end = @PeriodEnd AND sr.grid_area_code = '344')
                """, new { PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 2, 1) });
            await conn.ExecuteAsync("""
                DELETE FROM settlement.settlement_line WHERE settlement_run_id IN (
                    SELECT sr.id FROM settlement.settlement_run sr
                    JOIN settlement.billing_period bp ON sr.billing_period_id = bp.id
                    WHERE bp.period_start = @PeriodStart AND bp.period_end = @PeriodEnd AND sr.grid_area_code = '344')
                """, new { PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 2, 1) });
            await conn.ExecuteAsync("""
                DELETE FROM settlement.settlement_run sr USING settlement.billing_period bp
                WHERE sr.billing_period_id = bp.id AND bp.period_start = @PeriodStart AND bp.period_end = @PeriodEnd AND sr.grid_area_code = '344'
                """, new { PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 2, 1) });
            await conn.ExecuteAsync("DELETE FROM portfolio.contract WHERE gsrn = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM portfolio.supply_period WHERE gsrn = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM portfolio.signup WHERE gsrn = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM lifecycle.process_event WHERE process_request_id IN (SELECT id FROM lifecycle.process_request WHERE gsrn = @Gsrn)", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM lifecycle.process_request WHERE gsrn = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM metering.metering_data_history WHERE metering_point_id = @Gsrn", new { Gsrn });
            await conn.ExecuteAsync("DELETE FROM metering.metering_data WHERE metering_point_id = @Gsrn", new { Gsrn });
        }

        // ──── 1. SEED: tariffs, spot prices, grid area ────
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);
        await SeedTariffsAsync(ct);
        await SeedSpotPricesAsync(periodStart, periodEnd, ct);

        // ──── 2. ARRANGE: customer + product + metering point + contract ────
        var customer = await _portfolio.CreateCustomerAsync("Correction Test Kunde", "5555555555", "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot Correction", "spot", 4.0m, null, 39.00m, ct);

        var mp = new MeteringPoint(Gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        try { await _portfolio.CreateMeteringPointAsync(mp, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or PostgresException) { }

        try { await _portfolio.CreateContractAsync(customer.Id, Gsrn, product.Id, "monthly", "post_payment", periodStart, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or PostgresException) { }

        // ──── 3. Create completed process ────
        var stateMachine = new ProcessStateMachine(_processRepo, clock);
        var process = await stateMachine.CreateRequestAsync(Gsrn, "supplier_switch", periodStart, ct);
        await stateMachine.MarkSentAsync(process.Id, "corr-correction-test", ct);
        await stateMachine.MarkAcknowledgedAsync(process.Id, ct);
        await stateMachine.MarkCompletedAsync(process.Id, ct);

        // ──── 4. Store original metering data (744 hours in January) ────
        var originalRows = new List<MeteringDataRow>();
        var start = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hours = (int)(end - start).TotalHours;

        for (var i = 0; i < hours; i++)
        {
            var ts = start.AddHours(i);
            originalRows.Add(new MeteringDataRow(ts, "PT1H", 0.500m, "A03", "test-correction"));
        }
        await _meteringRepo.StoreTimeSeriesAsync(Gsrn, originalRows, ct);

        // ──── 5. Run original settlement via engine + store directly ────
        var dataLoader = new SettlementDataLoader(_meteringRepo, _spotPriceRepo, _tariffRepo);
        var engine = new SettlementEngine();
        var resultStore = new SettlementResultStore(TestDatabase.ConnectionString);

        var input = await dataLoader.LoadAsync(
            Gsrn, "344", "DK1", periodStart, periodEnd,
            product.MarginOrePerKwh / 100m, (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth, ct);

        var request = new SettlementRequest(
            input.MeteringPointId, input.PeriodStart, input.PeriodEnd,
            input.Consumption, input.SpotPrices, input.GridTariffRates,
            input.SystemTariffRate, input.TransmissionTariffRate, input.ElectricityTaxRate,
            input.GridSubscriptionPerMonth, input.MarginPerKwh, input.SupplementPerKwh,
            input.SupplierSubscriptionPerMonth, input.Elvarme);

        var result = engine.Calculate(request);
        await resultStore.StoreAsync(Gsrn, "344", result, "monthly", ct);

        // Verify original settlement exists
        await using var assertConn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await assertConn.OpenAsync(ct);

        var originalRun = await assertConn.QuerySingleAsync<dynamic>(
            "SELECT id FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn });

        // ──── 6. Store REVISED metering data (higher consumption for first 24 hours) ────
        var revisedRows = new List<MeteringDataRow>();
        for (var i = 0; i < 24; i++)
        {
            var ts = start.AddHours(i);
            revisedRows.Add(new MeteringDataRow(ts, "PT1H", 0.750m, "A03", "test-correction-revised")); // 0.500 → 0.750 (+0.250 per hour)
        }
        var changedCount = await _meteringRepo.StoreTimeSeriesWithHistoryAsync(Gsrn, revisedRows, ct);
        changedCount.Should().Be(24, "24 hours of data should be detected as changed");

        // ──── 7. Trigger correction ────
        var correctionRepo = new CorrectionRepository(TestDatabase.ConnectionString);
        var correctionEngine = new CorrectionEngine();
        var correctionService = new CorrectionService(
            correctionEngine, correctionRepo, _meteringRepo,
            _spotPriceRepo, _tariffRepo, _portfolio, _billingRepo);

        var correctionRequest = new TriggerCorrectionRequest(Gsrn, periodStart, periodEnd, "Integration test correction");
        var correctionResult = await correctionService.TriggerCorrectionAsync(correctionRequest, ct);

        // ──── 8. ASSERT: correction batch exists ────
        correctionResult.Should().NotBeNull();
        correctionResult.MeteringPointId.Should().Be(Gsrn);
        correctionResult.TriggerType.Should().Be("manual");

        // ──── 9. ASSERT: correction lines have positive delta amounts (consumption increased) ────
        correctionResult.Lines.Should().NotBeEmpty("correction should produce delta lines");
        correctionResult.Lines.Should().Contain(l => l.ChargeType == "energy");
        correctionResult.Lines.Where(l => l.ChargeType == "energy")
            .Should().AllSatisfy(l => l.DeltaKwh.Should().BeGreaterThan(0, "energy delta kWh should be positive (consumption increased)"));

        // ──── 10. ASSERT: correction stored in database ────
        var dbCorrection = await assertConn.QueryFirstAsync<dynamic>(
            "SELECT original_run_id, trigger_type FROM settlement.correction_settlement WHERE correction_batch_id = @BatchId LIMIT 1",
            new { BatchId = correctionResult.CorrectionBatchId });

        ((string)dbCorrection.trigger_type).Should().Be("manual");

        // Original run ID should reference the original settlement run
        if (dbCorrection.original_run_id is not null)
            ((Guid)dbCorrection.original_run_id).Should().Be((Guid)originalRun.id);
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
        await _tariffRepo.SeedGridTariffAsync("344", "system", new DateOnly(2025, 1, 1), [new TariffRateRow(1, 0.054m)], ct);
        await _tariffRepo.SeedGridTariffAsync("344", "transmission", new DateOnly(2025, 1, 1), [new TariffRateRow(1, 0.049m)], ct);
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
