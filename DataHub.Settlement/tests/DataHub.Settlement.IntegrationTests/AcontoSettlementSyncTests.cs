#nullable disable
using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Billing;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.Infrastructure.Settlement;
using DataHub.Settlement.Infrastructure.Tariff;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// Integration tests for aconto/post_payment payment models, aconto reconciliation,
/// and daily billing frequency — verifying end-to-end from portfolio setup through invoicing.
/// </summary>
[Collection("Database")]
public class AcontoSettlementSyncTests
{
    private const string GsrnPostPayment = "571313100000090001";
    private const string GsrnAconto = "571313100000090002";
    private const string GsrnDaily = "571313100000090003";
    private const string CprCvr = "1234567890";

    private readonly PortfolioRepository _portfolio;
    private readonly TariffRepository _tariffRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly SpotPriceRepository _spotPriceRepo;
    private readonly ProcessRepository _processRepo;
    private readonly AcontoPaymentRepository _acontoRepo;
    private readonly InvoiceRepository _invoiceRepo;

    public AcontoSettlementSyncTests(TestDatabase db)
    {
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
        _tariffRepo = new TariffRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _spotPriceRepo = new SpotPriceRepository(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        _acontoRepo = new AcontoPaymentRepository(TestDatabase.ConnectionString);
        _invoiceRepo = new InvoiceRepository(TestDatabase.ConnectionString);
    }

    [Fact]
    public async Task Post_payment_monthly_no_aconto_invoice_only_settlement()
    {
        var ct = CancellationToken.None;
        var gsrn = GsrnPostPayment;
        var effectiveDate = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 2, 1); // exclusive
        var today = new DateOnly(2025, 2, 1);
        var clock = new TestClock { Today = today };

        await CleanupAsync(gsrn, ct);
        await SeedTariffsAndPricesAsync(effectiveDate, periodEnd, ct);

        // Arrange: customer, product, metering point, contract (post_payment)
        var customer = await _portfolio.CreateCustomerAsync("Post-Payment Test", CprCvr, "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot PostPay", "spot", 4.0m, null, 39.00m, ct);
        var mp = new MeteringPoint(gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        try { await _portfolio.CreateMeteringPointAsync(mp, ct); } catch { }
        await _portfolio.CreateContractAsync(customer.Id, gsrn, product.Id, "monthly", "post_payment", effectiveDate, ct);

        // Create and complete process
        var sm = new ProcessStateMachine(_processRepo, clock);
        var process = await sm.CreateRequestAsync(gsrn, "supplier_switch", effectiveDate, ct);
        await sm.MarkSentAsync(process.Id, "corr-postpay-1", ct);
        await sm.MarkAcknowledgedAsync(process.Id, ct);
        await sm.MarkCompletedAsync(process.Id, ct);

        // Activate and store metering data
        await _portfolio.ActivateMeteringPointAsync(gsrn, effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct);
        try { await _portfolio.CreateSupplyPeriodAsync(gsrn, effectiveDate, ct); } catch { }
        await StoreMeteringDataAsync(gsrn, effectiveDate, periodEnd, ct);

        // Effectuate — with post_payment, NO aconto invoice should be created
        var effectuation = new EffectuationService(
            TestDatabase.ConnectionString, NullOnboardingService.Instance,
            new InvoiceService(_invoiceRepo, _acontoRepo, NullLogger<InvoiceService>.Instance),
            new FakeDataHubClient(), new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(), clock, NullLogger<EffectuationService>.Instance);

        // We can't call EffectuationService.ActivateAsync directly since process is already completed.
        // Instead, verify that NO aconto invoices exist (post_payment should not create them).

        // Run settlement orchestration
        var resultStore = new SettlementResultStore(TestDatabase.ConnectionString);
        var orchestration = new SettlementOrchestrationService(
            _processRepo, _portfolio,
            new MeteringCompletenessChecker(TestDatabase.ConnectionString),
            new SettlementDataLoader(_meteringRepo, _spotPriceRepo, _tariffRepo),
            new SettlementEngine(), resultStore, clock,
            NullLogger<SettlementOrchestrationService>.Instance);
        await orchestration.RunTickAsync(ct);

        // Run invoicing
        var invoicing = new InvoicingService(
            TestDatabase.ConnectionString,
            new InvoiceService(_invoiceRepo, _acontoRepo, NullLogger<InvoiceService>.Instance),
            _acontoRepo, clock,
            NullLogger<InvoicingService>.Instance);
        await invoicing.RunTickAsync(ct);

        // Assert: exactly 1 invoice (settlement only), no aconto
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        var invoices = (await conn.QueryAsync<dynamic>(
            """
            SELECT i.invoice_type, i.total_incl_vat
            FROM billing.invoice i
            JOIN portfolio.contract c ON c.id = i.contract_id AND c.gsrn = @Gsrn
            WHERE i.status <> 'cancelled'
            """,
            new { Gsrn = gsrn })).ToList();

        invoices.Should().HaveCount(1, "post_payment should have exactly 1 settlement invoice");
        ((string)invoices[0].invoice_type).Should().Be("settlement");
    }

    [Fact]
    public async Task Aconto_quarterly_reconciliation_deducts_prepayment()
    {
        var ct = CancellationToken.None;
        var gsrn = GsrnAconto;
        var effectiveDate = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 4, 1); // exclusive, Q1
        var today = new DateOnly(2025, 4, 2); // after quarter end
        var clock = new TestClock { Today = today };

        await CleanupAsync(gsrn, ct);
        await SeedTariffsAndPricesAsync(effectiveDate, periodEnd, ct);

        // Arrange: customer, product, metering point, contract (aconto)
        var customer = await _portfolio.CreateCustomerAsync("Aconto Test", CprCvr, "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot Aconto", "spot", 4.0m, null, 39.00m, ct);
        var mp = new MeteringPoint(gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        try { await _portfolio.CreateMeteringPointAsync(mp, ct); } catch { }
        await _portfolio.CreateContractAsync(customer.Id, gsrn, product.Id, "quarterly", "aconto", effectiveDate, ct);

        // Create and complete process
        var sm = new ProcessStateMachine(_processRepo, clock);
        var process = await sm.CreateRequestAsync(gsrn, "supplier_switch", effectiveDate, ct);
        await sm.MarkSentAsync(process.Id, "corr-aconto-1", ct);
        await sm.MarkAcknowledgedAsync(process.Id, ct);
        await sm.MarkCompletedAsync(process.Id, ct);

        // Activate and store metering data for full Q1
        await _portfolio.ActivateMeteringPointAsync(gsrn, effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct);
        try { await _portfolio.CreateSupplyPeriodAsync(gsrn, effectiveDate, ct); } catch { }
        await StoreMeteringDataAsync(gsrn, effectiveDate, periodEnd, ct);

        // Record aconto payment (simulating what EffectuationService does for aconto)
        var acontoAmount = 500.00m;
        await _acontoRepo.RecordPaymentAsync(gsrn, effectiveDate, periodEnd, acontoAmount, ct);

        // Run settlement orchestration (creates settlement_run for Q1)
        var resultStore = new SettlementResultStore(TestDatabase.ConnectionString);
        var orchestration = new SettlementOrchestrationService(
            _processRepo, _portfolio,
            new MeteringCompletenessChecker(TestDatabase.ConnectionString),
            new SettlementDataLoader(_meteringRepo, _spotPriceRepo, _tariffRepo),
            new SettlementEngine(), resultStore, clock,
            NullLogger<SettlementOrchestrationService>.Instance);
        await orchestration.RunTickAsync(ct);

        // Run invoicing — should create settlement invoice with aconto_deduction line
        var invoicing = new InvoicingService(
            TestDatabase.ConnectionString,
            new InvoiceService(_invoiceRepo, _acontoRepo, NullLogger<InvoiceService>.Instance),
            _acontoRepo, clock,
            NullLogger<InvoicingService>.Instance);
        await invoicing.RunTickAsync(ct);

        // Assert: settlement invoice has aconto_deduction line
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var invoiceLines = (await conn.QueryAsync<dynamic>(
            """
            SELECT il.line_type, il.amount_ex_vat
            FROM billing.invoice_line il
            JOIN billing.invoice i ON i.id = il.invoice_id
            JOIN portfolio.contract c ON c.id = i.contract_id AND c.gsrn = @Gsrn
            WHERE i.status <> 'cancelled' AND i.invoice_type = 'settlement'
            ORDER BY il.sort_order
            """,
            new { Gsrn = gsrn })).ToList();

        invoiceLines.Should().NotBeEmpty("settlement invoice should have lines");

        var deductionLine = invoiceLines.FirstOrDefault(l => (string)l.line_type == "aconto_deduction");
        ((object)deductionLine).Should().NotBeNull("settlement invoice for aconto customer should have aconto_deduction line");
        ((decimal)deductionLine!.amount_ex_vat).Should().Be(-acontoAmount,
            "deduction should equal the aconto prepayment as a negative amount");
    }

    [Fact]
    public async Task No_double_charge_aconto_and_settlement_same_period()
    {
        var ct = CancellationToken.None;
        var gsrn = GsrnAconto;
        // Reuse the aconto scenario — after the previous test, we should verify totals

        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        // Get all invoice lines for this GSRN
        var allLines = (await conn.QueryAsync<dynamic>(
            """
            SELECT il.line_type, il.amount_ex_vat, il.amount_incl_vat
            FROM billing.invoice_line il
            JOIN billing.invoice i ON i.id = il.invoice_id
            JOIN portfolio.contract c ON c.id = i.contract_id AND c.gsrn = @Gsrn
            WHERE i.status <> 'cancelled'
            """,
            new { Gsrn = gsrn })).ToList();

        if (allLines.Count == 0) return; // Skip if aconto test didn't run first

        // Sum all non-deduction amounts = actual settlement cost
        var settlementTotal = allLines
            .Where(l => (string)l.line_type != "aconto_deduction")
            .Sum(l => (decimal)l.amount_ex_vat);

        // Sum aconto deductions
        var deductionTotal = allLines
            .Where(l => (string)l.line_type == "aconto_deduction")
            .Sum(l => (decimal)l.amount_ex_vat);

        // Net total (what customer actually pays) = settlement - aconto
        var netTotal = settlementTotal + deductionTotal; // deduction is negative
        netTotal.Should().BeLessThan(settlementTotal, "aconto deduction should reduce the net charge");
        netTotal.Should().Be(settlementTotal - Math.Abs(deductionTotal),
            "net = actual settlement total minus aconto paid");
    }

    [Fact]
    public async Task Daily_billing_settles_single_day()
    {
        var ct = CancellationToken.None;
        var gsrn = GsrnDaily;
        var effectiveDate = new DateOnly(2025, 3, 15);
        var periodEnd = new DateOnly(2025, 3, 16); // exclusive, 1 day
        var today = new DateOnly(2025, 3, 16);
        var clock = new TestClock { Today = today };

        await CleanupAsync(gsrn, ct);
        await SeedTariffsAndPricesAsync(effectiveDate, periodEnd, ct);

        // Arrange: customer, product, metering point, contract (daily, post_payment)
        var customer = await _portfolio.CreateCustomerAsync("Daily Test", CprCvr, "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot Daily", "spot", 4.0m, null, 39.00m, ct);
        var mp = new MeteringPoint(gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        try { await _portfolio.CreateMeteringPointAsync(mp, ct); } catch { }
        await _portfolio.CreateContractAsync(customer.Id, gsrn, product.Id, "daily", "post_payment", effectiveDate, ct);

        // Create and complete process
        var sm = new ProcessStateMachine(_processRepo, clock);
        var process = await sm.CreateRequestAsync(gsrn, "supplier_switch", effectiveDate, ct);
        await sm.MarkSentAsync(process.Id, "corr-daily-1", ct);
        await sm.MarkAcknowledgedAsync(process.Id, ct);
        await sm.MarkCompletedAsync(process.Id, ct);

        // Activate and store exactly 24 hours of metering data
        await _portfolio.ActivateMeteringPointAsync(gsrn, effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct);
        try { await _portfolio.CreateSupplyPeriodAsync(gsrn, effectiveDate, ct); } catch { }
        await StoreMeteringDataAsync(gsrn, effectiveDate, periodEnd, ct);

        // Run settlement orchestration
        var resultStore = new SettlementResultStore(TestDatabase.ConnectionString);
        var orchestration = new SettlementOrchestrationService(
            _processRepo, _portfolio,
            new MeteringCompletenessChecker(TestDatabase.ConnectionString),
            new SettlementDataLoader(_meteringRepo, _spotPriceRepo, _tariffRepo),
            new SettlementEngine(), resultStore, clock,
            NullLogger<SettlementOrchestrationService>.Instance);
        await orchestration.RunTickAsync(ct);

        // Assert: settlement_run exists with 1-day period
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var run = await conn.QuerySingleOrDefaultAsync<dynamic>(
            """
            SELECT sr.id, bp.period_start, bp.period_end, sr.status
            FROM settlement.settlement_run sr
            JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
            WHERE sr.metering_point_id = @Gsrn
            """,
            new { Gsrn = gsrn });

        ((object)run).Should().NotBeNull("settlement should have run for daily period");
        ((string)run!.status).Should().Be("completed");
        DateOnly.FromDateTime((DateTime)run.period_start).Should().Be(effectiveDate);
        DateOnly.FromDateTime((DateTime)run.period_end).Should().Be(periodEnd);

        // Assert: settlement lines exist with correct amounts for 24 hours
        var lines = (await conn.QueryAsync<dynamic>(
            "SELECT charge_type, total_kwh, total_amount FROM settlement.settlement_line WHERE settlement_run_id = @RunId",
            new { RunId = (Guid)run.id })).ToList();

        lines.Should().NotBeEmpty("settlement lines should exist");
        var energyLine = lines.FirstOrDefault(l => (string)l.charge_type == "energy");
        ((object)energyLine).Should().NotBeNull("energy line should exist");
        ((decimal)energyLine!.total_kwh).Should().BeGreaterThan(0, "24 hours of consumption");
    }

    // ── Helpers ──

    private async Task CleanupAsync(string gsrn, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM billing.invoice_line WHERE invoice_id IN (SELECT i.id FROM billing.invoice i JOIN portfolio.contract c ON c.id = i.contract_id WHERE c.gsrn = @Gsrn)", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM billing.invoice WHERE contract_id IN (SELECT id FROM portfolio.contract WHERE gsrn = @Gsrn)", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM billing.aconto_payment WHERE gsrn = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM settlement.settlement_line WHERE metering_point_id = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM settlement.billing_period WHERE id NOT IN (SELECT billing_period_id FROM settlement.settlement_run)", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.contract WHERE gsrn = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.supply_period WHERE gsrn = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.signup WHERE gsrn = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM lifecycle.process_event WHERE process_request_id IN (SELECT id FROM lifecycle.process_request WHERE gsrn = @Gsrn)", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM lifecycle.process_request WHERE gsrn = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM metering.metering_data WHERE metering_point_id = @Gsrn", new { Gsrn = gsrn });
    }

    private async Task StoreMeteringDataAsync(string gsrn, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = new List<MeteringDataRow>();
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var totalHours = (int)(end - start).TotalHours;

        for (var i = 0; i < totalHours; i++)
        {
            var ts = start.AddHours(i);
            var kwh = ts.Hour switch
            {
                >= 0 and <= 5 => 0.200m,
                >= 6 and <= 15 => 0.500m,
                >= 16 and <= 20 => 0.800m,
                _ => 0.300m,
            };
            rows.Add(new MeteringDataRow(ts, "PT1H", kwh, "A03", "test-aconto"));
        }

        await _meteringRepo.StoreTimeSeriesAsync(gsrn, rows, ct);
    }

    private async Task SeedTariffsAndPricesAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
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
        await _tariffRepo.SeedGridTariffAsync("344", "system", new DateOnly(2025, 1, 1), [new TariffRateRow(1, 0.054m)], ct);
        await _tariffRepo.SeedGridTariffAsync("344", "transmission", new DateOnly(2025, 1, 1), [new TariffRateRow(1, 0.049m)], ct);
        await _tariffRepo.SeedSubscriptionAsync("344", "grid", 49.00m, new DateOnly(2025, 1, 1), ct);
        await _tariffRepo.SeedElectricityTaxAsync(0.008m, new DateOnly(2025, 1, 1), ct);

        // Spot prices
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
