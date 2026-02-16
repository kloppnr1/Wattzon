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
using DataHub.Settlement.Infrastructure.Messaging;
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
/// Tests that EffectuationService settles closed billing periods at effectuation time
/// instead of unconditionally creating aconto invoices for them.
/// </summary>
[Collection("Database")]
public class EffectuationSettlementTests : IClassFixture<TestDatabase>
{
    private const string Gsrn = "571313100000099999";
    private const string GridAreaCode = "344";
    private const string PriceArea = "DK1";

    private readonly PortfolioRepository _portfolio;
    private readonly ProcessRepository _processRepo;
    private readonly TariffRepository _tariffRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly SpotPriceRepository _spotPriceRepo;

    public EffectuationSettlementTests(TestDatabase db)
    {
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        _tariffRepo = new TariffRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _spotPriceRepo = new SpotPriceRepository(TestDatabase.ConnectionString);
    }

    [Fact]
    public async Task Retroactive_effectuation_settles_closed_period_instead_of_aconto()
    {
        var ct = CancellationToken.None;

        // Today is Monday Feb 23 — the week Feb 9 (Mon)–Feb 16 (Mon exclusive) is closed
        var clock = new TestClock { Today = new DateOnly(2026, 2, 23) };
        var effectiveDate = new DateOnly(2026, 2, 9); // Monday — week ends Feb 16 (exclusive)

        // Seed reference data
        await _portfolio.EnsureGridAreaAsync(GridAreaCode, "5790000392261", "N1 A/S", PriceArea, ct);
        await SeedTariffsAsync(new DateOnly(2026, 2, 1), ct);
        await SeedSpotPricesAsync(new DateOnly(2026, 2, 9), 7 * 24, ct); // 1 week of prices
        var product = await _portfolio.CreateProductAsync("Spot Test", "spot", 4.0m, null, 39.00m, ct);
        var customer = await _portfolio.CreateCustomerAsync("Test Retroaktiv", "0101901111", "private", null, ct);

        var mp = new MeteringPoint(Gsrn, "E17", "flex", GridAreaCode, "5790000392261", PriceArea, "connected");
        await _portfolio.CreateMeteringPointAsync(mp, ct);

        // Create process and advance to effectuation_pending
        var stateMachine = new ProcessStateMachine(_processRepo, clock);
        var process = await stateMachine.CreateRequestAsync(Gsrn, "supplier_switch", effectiveDate, ct);
        await stateMachine.MarkSentAsync(process.Id, Guid.NewGuid().ToString(), ct);
        await stateMachine.MarkAcknowledgedAsync(process.Id, ct);

        // Create signup linked to the process
        var signupId = await SeedSignupAsync(process.Id, Gsrn, customer.Id, product.Id, effectiveDate, "weekly", "aconto", ct);

        // Pre-store metering data for the closed period (Feb 9 00:00 – Feb 16 00:00)
        await SeedMeteringDataAsync(Gsrn, new DateOnly(2026, 2, 9), 7 * 24, ct);

        // Build the EffectuationService with a real SettlementTriggerService
        var settlementTrigger = CreateSettlementTriggerService(clock);
        var invoiceService = CreateInvoiceService();
        var effectuation = new EffectuationService(
            TestDatabase.ConnectionString,
            NullOnboardingService.Instance,
            invoiceService,
            new FakeDataHubClient(),
            new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(),
            clock,
            NullLogger<EffectuationService>.Instance,
            settlementTrigger);

        // ACT
        await effectuation.ActivateAsync(
            process.Id, signupId, Gsrn, effectiveDate,
            "supplier_switch", null, ct);

        // ASSERT: settlement_run exists for the closed period
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var settlementRunCount = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM settlement.settlement_run sr
            JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
            WHERE sr.metering_point_id = @Gsrn AND bp.period_start = @Start AND bp.period_end = @End
            """,
            new { Gsrn, Start = effectiveDate, End = new DateOnly(2026, 2, 16) });
        settlementRunCount.Should().Be(1, "closed period should be settled at effectuation time");

        // ASSERT: no aconto prepayment invoice for the closed period
        var acontoCount = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM billing.invoice_line il
            JOIN billing.invoice i ON i.id = il.invoice_id
            WHERE il.gsrn = @Gsrn AND il.line_type = 'aconto_prepayment'
            """,
            new { Gsrn });
        acontoCount.Should().Be(0, "closed period should not get an aconto prepayment invoice");
    }

    [Fact]
    public async Task Current_period_effectuation_creates_aconto_not_settlement()
    {
        var ct = CancellationToken.None;
        var gsrn = "571313100000099998";

        // Today is Monday Feb 23 — effective date is also Feb 23 (Monday)
        // Week period: Feb 23–Mar 2 (exclusive), which is still open
        var clock = new TestClock { Today = new DateOnly(2026, 2, 23) };
        var effectiveDate = new DateOnly(2026, 2, 23); // Monday — week ends Mar 2 (exclusive)

        // Seed reference data
        await _portfolio.EnsureGridAreaAsync(GridAreaCode, "5790000392261", "N1 A/S", PriceArea, ct);
        var product = await _portfolio.CreateProductAsync("Spot Test 2", "spot", 4.0m, null, 39.00m, ct);
        var customer = await _portfolio.CreateCustomerAsync("Test Aktuelt", "0101902222", "private", null, ct);

        var mp = new MeteringPoint(gsrn, "E17", "flex", GridAreaCode, "5790000392261", PriceArea, "connected");
        await _portfolio.CreateMeteringPointAsync(mp, ct);

        // Create process and advance to effectuation_pending
        var stateMachine = new ProcessStateMachine(_processRepo, clock);
        var process = await stateMachine.CreateRequestAsync(gsrn, "supplier_switch", effectiveDate, ct);
        await stateMachine.MarkSentAsync(process.Id, Guid.NewGuid().ToString(), ct);
        await stateMachine.MarkAcknowledgedAsync(process.Id, ct);

        // Create signup linked to the process
        var signupId = await SeedSignupAsync(process.Id, gsrn, customer.Id, product.Id, effectiveDate, "weekly", "aconto", ct);

        // No settlement trigger needed — period is open so no settlement should happen
        var settlementTrigger = CreateSettlementTriggerService(clock);
        var invoiceService = CreateInvoiceService();
        var effectuation = new EffectuationService(
            TestDatabase.ConnectionString,
            NullOnboardingService.Instance,
            invoiceService,
            new FakeDataHubClient(),
            new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(),
            clock,
            NullLogger<EffectuationService>.Instance,
            settlementTrigger);

        // ACT
        await effectuation.ActivateAsync(
            process.Id, signupId, gsrn, effectiveDate,
            "supplier_switch", null, ct);

        // ASSERT: no settlement run (period is still open)
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var settlementRunCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM settlement.settlement_run WHERE metering_point_id = @Gsrn",
            new { Gsrn = gsrn });
        settlementRunCount.Should().Be(0, "open period should not be settled");

        // ASSERT: aconto prepayment invoice created
        var acontoCount = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM billing.invoice_line il
            JOIN billing.invoice i ON i.id = il.invoice_id
            WHERE il.gsrn = @Gsrn AND il.line_type = 'aconto_prepayment'
            """,
            new { Gsrn = gsrn });
        acontoCount.Should().Be(1, "open period with aconto payment model should get an aconto prepayment invoice");
    }

    [Fact]
    public async Task Direct_payment_skips_aconto_and_settles_closed_period()
    {
        var ct = CancellationToken.None;
        var gsrn = "571313100000099997";

        // Today is Feb 23 — effective date Feb 9 (Monday) → closed week Feb 9–16
        var clock = new TestClock { Today = new DateOnly(2026, 2, 23) };
        var effectiveDate = new DateOnly(2026, 2, 9);

        // Seed reference data
        await _portfolio.EnsureGridAreaAsync(GridAreaCode, "5790000392261", "N1 A/S", PriceArea, ct);
        await SeedTariffsAsync(new DateOnly(2026, 2, 1), ct);
        await SeedSpotPricesAsync(new DateOnly(2026, 2, 9), 7 * 24, ct);
        var product = await _portfolio.CreateProductAsync("Spot Test 3", "spot", 4.0m, null, 39.00m, ct);
        var customer = await _portfolio.CreateCustomerAsync("Test Direkte", "0101903333", "private", null, ct);

        var mp = new MeteringPoint(gsrn, "E17", "flex", GridAreaCode, "5790000392261", PriceArea, "connected");
        await _portfolio.CreateMeteringPointAsync(mp, ct);

        // Create process and advance to effectuation_pending
        var stateMachine = new ProcessStateMachine(_processRepo, clock);
        var process = await stateMachine.CreateRequestAsync(gsrn, "supplier_switch", effectiveDate, ct);
        await stateMachine.MarkSentAsync(process.Id, Guid.NewGuid().ToString(), ct);
        await stateMachine.MarkAcknowledgedAsync(process.Id, ct);

        // Create signup with post_payment (direct payment, not aconto)
        var signupId = await SeedSignupAsync(process.Id, gsrn, customer.Id, product.Id, effectiveDate, "weekly", "post_payment", ct);

        // Pre-store metering data for the closed period (Feb 9 – Feb 16)
        await SeedMeteringDataAsync(gsrn, new DateOnly(2026, 2, 9), 7 * 24, ct);

        var settlementTrigger = CreateSettlementTriggerService(clock);
        var invoiceService = CreateInvoiceService();
        var effectuation = new EffectuationService(
            TestDatabase.ConnectionString,
            NullOnboardingService.Instance,
            invoiceService,
            new FakeDataHubClient(),
            new Infrastructure.DataHub.BrsRequestBuilder(),
            new NullMessageRepository(),
            clock,
            NullLogger<EffectuationService>.Instance,
            settlementTrigger);

        // ACT
        await effectuation.ActivateAsync(
            process.Id, signupId, gsrn, effectiveDate,
            "supplier_switch", null, ct);

        // ASSERT: settlement_run exists (closed period settled)
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var settlementRunCount = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM settlement.settlement_run sr
            JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
            WHERE sr.metering_point_id = @Gsrn AND bp.period_start = @Start AND bp.period_end = @End
            """,
            new { Gsrn = gsrn, Start = effectiveDate, End = new DateOnly(2026, 2, 16) });
        settlementRunCount.Should().Be(1, "closed period should be settled even for direct payment");

        // ASSERT: no aconto prepayment invoice (direct payment never gets aconto)
        var acontoCount = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM billing.invoice_line il
            JOIN billing.invoice i ON i.id = il.invoice_id
            WHERE il.gsrn = @Gsrn AND il.line_type = 'aconto_prepayment'
            """,
            new { Gsrn = gsrn });
        acontoCount.Should().Be(0, "direct payment model should never create aconto prepayment invoices");
    }

    // ──── Helper methods ────

    private SettlementTriggerService CreateSettlementTriggerService(TestClock clock)
    {
        var cs = TestDatabase.ConnectionString;
        return new SettlementTriggerService(
            _processRepo,
            _portfolio,
            new MeteringCompletenessChecker(cs),
            new SettlementDataLoader(_meteringRepo, _spotPriceRepo, _tariffRepo),
            new SettlementEngine(),
            new SettlementResultStore(cs),
            clock,
            NullLogger<SettlementTriggerService>.Instance);
    }

    private static IInvoiceService CreateInvoiceService()
    {
        var cs = TestDatabase.ConnectionString;
        return new InvoiceService(
            new InvoiceRepository(cs),
            NullLogger<InvoiceService>.Instance);
    }

    private static async Task<Guid> SeedSignupAsync(
        Guid processId, string gsrn, Guid? customerId, Guid productId,
        DateOnly effectiveDate, string billingFrequency, string paymentModel,
        CancellationToken ct)
    {
        var signupId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO portfolio.signup
                (id, signup_number, dar_id, gsrn, customer_id, product_id, process_request_id,
                 type, effective_date, status, billing_frequency, payment_model,
                 customer_name, customer_cpr_cvr, customer_contact_type)
            VALUES
                (@Id, @SignupNumber, @DarId, @Gsrn, @CustomerId, @ProductId, @ProcessId,
                 'switch', @EffectiveDate, 'registered', @BillingFrequency, @PaymentModel,
                 'Test Kunde', '0101900000', 'person')
            """,
            new
            {
                Id = signupId,
                SignupNumber = $"SGN-TEST-{signupId.ToString()[..8]}",
                DarId = $"0a3f5000-test-0000-0000-{signupId.ToString()[..12]}",
                Gsrn = gsrn,
                CustomerId = customerId,
                ProductId = productId,
                ProcessId = processId,
                EffectiveDate = effectiveDate,
                BillingFrequency = billingFrequency,
                PaymentModel = paymentModel,
            });
        return signupId;
    }

    private async Task SeedMeteringDataAsync(string gsrn, DateOnly startDate, int hours, CancellationToken ct)
    {
        var rows = new List<MeteringDataRow>();
        var start = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        for (var i = 0; i < hours; i++)
        {
            rows.Add(new MeteringDataRow(start.AddHours(i), "PT1H", 0.5m, null, "test-msg-001"));
        }
        await _meteringRepo.StoreTimeSeriesAsync(gsrn, rows, ct);
    }

    private async Task SeedTariffsAsync(DateOnly validFrom, CancellationToken ct)
    {
        var gridRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
        {
            >= 1 and <= 6 => 0.06m,
            >= 7 and <= 16 => 0.18m,
            >= 17 and <= 20 => 0.54m,
            _ => 0.06m,
        })).ToList();
        await _tariffRepo.SeedGridTariffAsync(GridAreaCode, "grid", validFrom, gridRates, ct);
        await _tariffRepo.SeedSubscriptionAsync(GridAreaCode, "grid", 49.00m, validFrom, ct);
        await _tariffRepo.SeedElectricityTaxAsync(0.008m, validFrom, ct);

        // System and transmission tariffs (flat rates, required by SettlementDataLoader)
        var systemRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, 0.054m)).ToList();
        await _tariffRepo.SeedGridTariffAsync(GridAreaCode, "system", validFrom, systemRates, ct);
        var transmissionRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, 0.049m)).ToList();
        await _tariffRepo.SeedGridTariffAsync(GridAreaCode, "transmission", validFrom, transmissionRates, ct);
    }

    private async Task SeedSpotPricesAsync(DateOnly startDate, int hours, CancellationToken ct)
    {
        var prices = new List<SpotPriceRow>();
        var start = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
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
            prices.Add(new SpotPriceRow(PriceArea, hour, price));
        }
        await _spotPriceRepo.StorePricesAsync(prices, ct);
    }
}
