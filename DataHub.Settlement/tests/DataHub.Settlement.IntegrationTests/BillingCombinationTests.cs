#nullable disable
using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Billing;
using DataHub.Settlement.Application.Lifecycle;
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
/// Integration tests verifying correct billing for all 6 billing_frequency × payment_model
/// combinations by advancing the clock past period boundaries.
///
/// For each combination:
///   1. Before the period boundary → no settlement run, no invoice
///   2. After the period boundary → settlement run completes, correct invoice created
///   3. Post-payment invoices have settlement lines only
///   4. Aconto invoices have settlement lines + deduction + prepayment
/// </summary>
[Collection("Database")]
public class BillingCombinationTests : IClassFixture<TestDatabase>
{
    private readonly PortfolioRepository _portfolio;
    private readonly TariffRepository _tariffRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly SpotPriceRepository _spotPriceRepo;
    private readonly ProcessRepository _processRepo;
    private readonly AcontoPaymentRepository _acontoRepo;
    private readonly InvoiceRepository _invoiceRepo;

    public BillingCombinationTests(TestDatabase db)
    {
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
        _tariffRepo = new TariffRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _spotPriceRepo = new SpotPriceRepository(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        _acontoRepo = new AcontoPaymentRepository(TestDatabase.ConnectionString);
        _invoiceRepo = new InvoiceRepository(TestDatabase.ConnectionString);
    }

    /// <summary>
    /// All 6 combinations: (frequency, paymentModel, effectiveDate, beforeBoundary, gsrn)
    /// </summary>
    public static IEnumerable<object[]> BeforeBoundary()
    {
        yield return new object[] { "weekly",    "post_payment", new DateOnly(2025,1,6),  new DateOnly(2025,1,12), "571313100000060001" };
        yield return new object[] { "weekly",    "aconto",       new DateOnly(2025,1,6),  new DateOnly(2025,1,12), "571313100000060002" };
        yield return new object[] { "monthly",   "post_payment", new DateOnly(2025,1,1),  new DateOnly(2025,1,31), "571313100000060003" };
        yield return new object[] { "monthly",   "aconto",       new DateOnly(2025,1,1),  new DateOnly(2025,1,31), "571313100000060004" };
        yield return new object[] { "quarterly", "post_payment", new DateOnly(2025,3,1),  new DateOnly(2025,3,31), "571313100000060005" };
        yield return new object[] { "quarterly", "aconto",       new DateOnly(2025,3,1),  new DateOnly(2025,3,31), "571313100000060006" };
    }

    /// <summary>
    /// All 6 combinations: (frequency, paymentModel, effectiveDate, afterBoundary, gsrn)
    /// </summary>
    public static IEnumerable<object[]> AfterBoundary()
    {
        yield return new object[] { "weekly",    "post_payment", new DateOnly(2025,1,6),  new DateOnly(2025,1,13), "571313100000060001" };
        yield return new object[] { "weekly",    "aconto",       new DateOnly(2025,1,6),  new DateOnly(2025,1,13), "571313100000060002" };
        yield return new object[] { "monthly",   "post_payment", new DateOnly(2025,1,1),  new DateOnly(2025,2,1),  "571313100000060003" };
        yield return new object[] { "monthly",   "aconto",       new DateOnly(2025,1,1),  new DateOnly(2025,2,1),  "571313100000060004" };
        yield return new object[] { "quarterly", "post_payment", new DateOnly(2025,3,1),  new DateOnly(2025,4,1),  "571313100000060005" };
        yield return new object[] { "quarterly", "aconto",       new DateOnly(2025,3,1),  new DateOnly(2025,4,1),  "571313100000060006" };
    }

    // ── Time-gate: no invoice before the period boundary ──

    [Theory]
    [MemberData(nameof(BeforeBoundary))]
    public async Task No_invoice_before_period_boundary(
        string frequency, string paymentModel,
        DateOnly effectiveDate, DateOnly beforeBoundary, string gsrn)
    {
        var ct = CancellationToken.None;
        var clock = new TestClock { Today = beforeBoundary };

        await SetupPortfolioAsync(gsrn, frequency, paymentModel, effectiveDate, clock, ct);

        // Run full pipeline with clock BEFORE the boundary
        await CreateOrchestration(clock).RunTickAsync(ct);
        await CreateInvoicing(clock).RunTickAsync(ct);

        // Assert: no invoices — period hasn't closed yet
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        var invoiceCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM billing.invoice i JOIN portfolio.contract c ON c.id = i.contract_id WHERE c.gsrn = @Gsrn AND i.status <> 'cancelled'",
            new { Gsrn = gsrn });

        invoiceCount.Should().Be(0,
            $"{frequency}/{paymentModel}: no invoice should exist before period boundary ({beforeBoundary})");
    }

    // ── After boundary: correct invoice created for each combination ──

    [Theory]
    [MemberData(nameof(AfterBoundary))]
    public async Task Creates_correct_invoice_after_period_boundary(
        string frequency, string paymentModel,
        DateOnly effectiveDate, DateOnly afterBoundary, string gsrn)
    {
        var ct = CancellationToken.None;
        var clock = new TestClock { Today = afterBoundary };

        await SetupPortfolioAsync(gsrn, frequency, paymentModel, effectiveDate, clock, ct);

        // For aconto, seed a prepayment so we can verify deduction
        if (paymentModel == "aconto")
        {
            var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(effectiveDate, frequency);
            await _acontoRepo.RecordPaymentAsync(gsrn, effectiveDate, periodEnd, 100.00m, ct);
        }

        // Run full pipeline with clock AFTER the boundary
        await CreateOrchestration(clock).RunTickAsync(ct);

        await CreateInvoicing(clock).RunTickAsync(ct);

        // Assert: exactly 1 invoice
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        var invoices = (await conn.QueryAsync<dynamic>(
            """
            SELECT i.id, i.invoice_type, i.total_incl_vat
            FROM billing.invoice i
            JOIN portfolio.contract c ON c.id = i.contract_id
            WHERE c.gsrn = @Gsrn AND i.status <> 'cancelled'
            """,
            new { Gsrn = gsrn })).ToList();

        invoices.Should().HaveCount(1,
            $"{frequency}/{paymentModel}: should have exactly 1 invoice after boundary ({afterBoundary})");
        ((string)invoices[0].invoice_type).Should().Be("settlement");
        ((decimal)invoices[0].total_incl_vat).Should().BeGreaterThan(0,
            "invoice total should be positive (energy + tariffs + VAT)");

        // Assert invoice lines
        var lines = (await conn.QueryAsync<dynamic>(
            """
            SELECT il.line_type, il.amount_ex_vat, il.amount_incl_vat
            FROM billing.invoice_line il
            WHERE il.invoice_id = @InvoiceId
            ORDER BY il.sort_order
            """,
            new { InvoiceId = (Guid)invoices[0].id })).ToList();

        lines.Should().NotBeEmpty("invoice should have settlement lines");

        var energyLine = lines.FirstOrDefault(l => (string)l.line_type == "energy");
        ((object)energyLine).Should().NotBeNull("should have an energy charge line");

        if (paymentModel == "aconto")
        {
            var deductionLine = lines.FirstOrDefault(l => (string)l.line_type == "aconto_deduction");
            ((object)deductionLine).Should().NotBeNull(
                $"{frequency}/aconto: should have aconto_deduction line");
            ((decimal)deductionLine!.amount_ex_vat).Should().Be(-100.00m,
                "deduction should equal the seeded aconto payment as a negative amount");

            var prepaymentLine = lines.FirstOrDefault(l => (string)l.line_type == "aconto_prepayment");
            ((object)prepaymentLine).Should().NotBeNull(
                $"{frequency}/aconto: should have aconto_prepayment line for next period");
        }
        else
        {
            var deductionLine = lines.FirstOrDefault(l => (string)l.line_type == "aconto_deduction");
            ((object)deductionLine).Should().BeNull(
                $"{frequency}/post_payment: should NOT have aconto_deduction");

            var prepaymentLine = lines.FirstOrDefault(l => (string)l.line_type == "aconto_prepayment");
            ((object)prepaymentLine).Should().BeNull(
                $"{frequency}/post_payment: should NOT have aconto_prepayment");
        }
    }

    // ── Idempotency: running invoicing twice doesn't create duplicates ──

    [Theory]
    [MemberData(nameof(AfterBoundary))]
    public async Task Running_invoicing_twice_does_not_duplicate(
        string frequency, string paymentModel,
        DateOnly effectiveDate, DateOnly afterBoundary, string gsrn)
    {
        var ct = CancellationToken.None;
        var clock = new TestClock { Today = afterBoundary };

        await SetupPortfolioAsync(gsrn, frequency, paymentModel, effectiveDate, clock, ct);

        if (paymentModel == "aconto")
        {
            var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(effectiveDate, frequency);
            await _acontoRepo.RecordPaymentAsync(gsrn, effectiveDate, periodEnd, 100.00m, ct);
        }

        var orchestration = CreateOrchestration(clock);
        var invoicing = CreateInvoicing(clock);

        // Run the full pipeline TWICE
        await orchestration.RunTickAsync(ct);
        await invoicing.RunTickAsync(ct);
        await invoicing.RunTickAsync(ct); // second run — should be idempotent

        // Assert: still exactly 1 invoice
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        var invoiceCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM billing.invoice i JOIN portfolio.contract c ON c.id = i.contract_id WHERE c.gsrn = @Gsrn AND i.status <> 'cancelled'",
            new { Gsrn = gsrn });

        invoiceCount.Should().Be(1,
            $"{frequency}/{paymentModel}: invoicing should be idempotent — running twice should still produce exactly 1 invoice");
    }

    // ── Setup helpers ──

    private async Task SetupPortfolioAsync(
        string gsrn, string frequency, string paymentModel,
        DateOnly effectiveDate, TestClock clock, CancellationToken ct)
    {
        await CleanupAsync(gsrn, ct);

        var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(effectiveDate, frequency);
        await SeedTariffsAndPricesAsync(effectiveDate, periodEnd, ct);

        var customer = await _portfolio.CreateCustomerAsync($"Test {gsrn}", gsrn[^10..], "private", null, ct);
        var product = await _portfolio.CreateProductAsync($"Spot {gsrn}", "spot", 4.0m, null, 39.00m, ct);
        var mp = new MeteringPoint(gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        try { await _portfolio.CreateMeteringPointAsync(mp, ct); } catch { /* already exists */ }
        await _portfolio.CreateContractAsync(customer.Id, gsrn, product.Id, frequency, paymentModel, effectiveDate, ct);

        // Create and complete process
        var sm = new ProcessStateMachine(_processRepo, clock);
        var process = await sm.CreateRequestAsync(gsrn, ProcessTypes.SupplierSwitch, effectiveDate, ct);
        await sm.MarkSentAsync(process.Id, $"corr-{gsrn}", ct);
        await sm.MarkAcknowledgedAsync(process.Id, ct);
        await sm.MarkCompletedAsync(process.Id, ct);

        // Activate and store metering data
        await _portfolio.ActivateMeteringPointAsync(gsrn, effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct);
        try { await _portfolio.CreateSupplyPeriodAsync(gsrn, effectiveDate, ct); } catch { /* already exists */ }
        await StoreMeteringDataAsync(gsrn, effectiveDate, periodEnd, ct);
    }

    private SettlementOrchestrationService CreateOrchestration(TestClock clock)
    {
        var trigger = new SettlementTriggerService(
            _processRepo, _portfolio,
            new MeteringCompletenessChecker(TestDatabase.ConnectionString),
            new SettlementDataLoader(_meteringRepo, _spotPriceRepo, _tariffRepo),
            new SettlementEngine(), new SettlementResultStore(TestDatabase.ConnectionString),
            clock, NullLogger<SettlementTriggerService>.Instance);
        return new(_processRepo, trigger, NullLogger<SettlementOrchestrationService>.Instance);
    }

    private InvoicingService CreateInvoicing(TestClock clock)
        => new(
            TestDatabase.ConnectionString,
            new InvoiceService(_invoiceRepo, _acontoRepo, NullLogger<InvoiceService>.Instance),
            _acontoRepo, clock,
            NullLogger<InvoicingService>.Instance);

    private async Task CleanupAsync(string gsrn, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM billing.invoice_line WHERE invoice_id IN (SELECT i.id FROM billing.invoice i JOIN portfolio.contract c ON c.id = i.contract_id WHERE c.gsrn = @Gsrn)", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM billing.invoice WHERE contract_id IN (SELECT id FROM portfolio.contract WHERE gsrn = @Gsrn)", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM billing.aconto_payment WHERE gsrn = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM settlement.settlement_line WHERE metering_point_id = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn = gsrn });
        await conn.ExecuteAsync("DELETE FROM settlement.billing_period WHERE id NOT IN (SELECT billing_period_id FROM settlement.settlement_run)");
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
            rows.Add(new MeteringDataRow(ts, "PT1H", kwh, "A03", "test-billing"));
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
