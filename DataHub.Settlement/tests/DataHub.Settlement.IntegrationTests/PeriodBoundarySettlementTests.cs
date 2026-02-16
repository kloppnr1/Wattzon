using Dapper;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
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
/// Integration tests verifying that period boundaries are handled correctly
/// with the exclusive periodEnd convention.
/// </summary>
[Collection("Database")]
public class PeriodBoundarySettlementTests
{
    private const string Gsrn = "571313100000097531";

    private readonly PortfolioRepository _portfolio;
    private readonly TariffRepository _tariffRepo;
    private readonly MeteringDataRepository _meteringRepo;
    private readonly SpotPriceRepository _spotPriceRepo;
    private readonly ProcessRepository _processRepo;

    public PeriodBoundarySettlementTests(TestDatabase db)
    {
        _portfolio = new PortfolioRepository(TestDatabase.ConnectionString);
        _tariffRepo = new TariffRepository(TestDatabase.ConnectionString);
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _spotPriceRepo = new SpotPriceRepository(TestDatabase.ConnectionString);
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
    }

    /// <summary>
    /// Sunday start with weekly billing = single-day period (Sun to Mon exclusive).
    /// Exclusive end = Monday. Data covers 24 hours of Sunday.
    /// </summary>
    [Fact]
    public async Task Weekly_sunday_start_settles_single_day_period()
    {
        var ct = CancellationToken.None;
        var effectiveDate = new DateOnly(2025, 1, 5); // Sunday
        effectiveDate.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        var exclusiveEnd = new DateOnly(2025, 1, 6); // Monday (exclusive end)
        var today = new DateOnly(2025, 1, 6); // Monday — periodEnd == today → settle
        var clock = new TestClock { Today = today };

        await CleanupAsync(ct);
        await SeedInfrastructureAsync(effectiveDate, exclusiveEnd, ct);
        var (customer, product) = await CreateEntitiesAsync(effectiveDate, ct);
        await CreateCompletedProcessAsync(effectiveDate, clock, ct);
        await ActivateAndSeedMeteringAsync(effectiveDate, exclusiveEnd, ct);

        await RunOrchestrationAsync(clock, ct);

        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var runCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn });
        runCount.Should().BeGreaterThan(0, "settlement_run should be created for single-day Sunday period");
    }

    /// <summary>
    /// Monday start with weekly billing = 7-day period (Mon to next Mon exclusive).
    /// Exclusive end = next Monday. Data covers 168 hours (Mon–Sun).
    /// </summary>
    [Fact]
    public async Task Weekly_full_week_includes_sunday_data()
    {
        var ct = CancellationToken.None;
        var effectiveDate = new DateOnly(2025, 1, 6); // Monday
        effectiveDate.DayOfWeek.Should().Be(DayOfWeek.Monday);
        var exclusiveEnd = new DateOnly(2025, 1, 13); // Next Monday (exclusive)
        var today = new DateOnly(2025, 1, 14); // Tuesday after
        var clock = new TestClock { Today = today };

        await CleanupAsync(ct);
        await SeedInfrastructureAsync(effectiveDate, exclusiveEnd, ct);
        var (customer, product) = await CreateEntitiesAsync(effectiveDate, ct);
        await CreateCompletedProcessAsync(effectiveDate, clock, ct);
        await ActivateAndSeedMeteringAsync(effectiveDate, exclusiveEnd, ct);

        await RunOrchestrationAsync(clock, ct);

        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var runCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn });
        runCount.Should().BeGreaterThan(0, "settlement_run should be created for full week including Sunday");
    }

    /// <summary>
    /// Full January (Jan 1 to Feb 1 exclusive) with monthly billing.
    /// Data covers 744 hours (31 days). Subscription = 49.00 DKK (full month).
    /// </summary>
    [Fact]
    public async Task Monthly_includes_last_day_of_month()
    {
        var ct = CancellationToken.None;
        var effectiveDate = new DateOnly(2025, 1, 1);
        var exclusiveEnd = new DateOnly(2025, 2, 1); // Feb 1 (exclusive end for January)
        var today = new DateOnly(2025, 2, 1);
        var clock = new TestClock { Today = today };

        await CleanupAsync(ct);
        await SeedInfrastructureAsync(effectiveDate, exclusiveEnd, ct);
        var (customer, product) = await CreateEntitiesAsync(effectiveDate, ct, billingFrequency: "monthly");
        await CreateCompletedProcessAsync(effectiveDate, clock, ct);
        await ActivateAndSeedMeteringAsync(effectiveDate, exclusiveEnd, ct);

        await RunOrchestrationAsync(clock, ct);

        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var runCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn });
        runCount.Should().BeGreaterThan(0, "settlement_run should be created for full January including Jan 31");

        if (runCount > 0)
        {
            var runId = await conn.ExecuteScalarAsync<Guid>(
                "SELECT id FROM settlement.settlement_run WHERE metering_point_id = @Gsrn LIMIT 1", new { Gsrn });
            var gridSub = await conn.ExecuteScalarAsync<decimal>(
                "SELECT total_amount FROM settlement.settlement_line WHERE settlement_run_id = @RunId AND charge_type = 'grid_subscription'",
                new { RunId = runId });
            gridSub.Should().Be(49.00m, "full month subscription should be 49.00 DKK");
        }
    }

    /// <summary>
    /// Settlement runs when periodEnd == today.
    /// With exclusive convention and `periodEnd > today` check, periodEnd == today means NOT >, so we settle.
    /// </summary>
    [Fact]
    public async Task Settlement_runs_when_period_end_equals_today()
    {
        var ct = CancellationToken.None;
        var effectiveDate = new DateOnly(2025, 1, 6); // Monday
        var exclusiveEnd = new DateOnly(2025, 1, 13); // Next Monday (exclusive)
        var today = new DateOnly(2025, 1, 13); // Same as exclusiveEnd
        var clock = new TestClock { Today = today };

        await CleanupAsync(ct);
        await SeedInfrastructureAsync(effectiveDate, exclusiveEnd, ct);
        var (customer, product) = await CreateEntitiesAsync(effectiveDate, ct);
        await CreateCompletedProcessAsync(effectiveDate, clock, ct);
        await ActivateAndSeedMeteringAsync(effectiveDate, exclusiveEnd, ct);

        await RunOrchestrationAsync(clock, ct);

        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);

        var runCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn });
        runCount.Should().BeGreaterThan(0, "settlement should run when periodEnd == today");
    }

    // ── Helpers ──

    private async Task CleanupAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(TestDatabase.ConnectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM settlement.correction_settlement WHERE metering_point_id = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM settlement.settlement_line WHERE metering_point_id = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM settlement.settlement_run WHERE metering_point_id = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.contract WHERE gsrn = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.supply_period WHERE gsrn = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.signup WHERE gsrn = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM lifecycle.process_event WHERE process_request_id IN (SELECT id FROM lifecycle.process_request WHERE gsrn = @Gsrn)", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM lifecycle.process_request WHERE gsrn = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM metering.metering_data WHERE metering_point_id = @Gsrn", new { Gsrn });
        await conn.ExecuteAsync("DELETE FROM portfolio.metering_point WHERE gsrn = @Gsrn", new { Gsrn });
    }

    private async Task SeedInfrastructureAsync(DateOnly periodStart, DateOnly exclusiveEnd, CancellationToken ct)
    {
        await _portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);

        // Tariffs
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

        // Spot prices — exclusiveEnd is already the day after the last day, use it directly
        var prices = new List<SpotPriceRow>();
        var start = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = exclusiveEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hours = (int)(end - start).TotalHours;
        for (var i = 0; i < hours; i++)
        {
            var ts = start.AddHours(i);
            var price = ts.Hour switch
            {
                >= 0 and <= 5 => 45m,
                >= 6 and <= 15 => 85m,
                >= 16 and <= 19 => 125m,
                _ => 55m,
            };
            prices.Add(new SpotPriceRow("DK1", ts, price));
        }
        await _spotPriceRepo.StorePricesAsync(prices, ct);
    }

    private async Task<(Customer customer, Product product)> CreateEntitiesAsync(
        DateOnly effectiveDate, CancellationToken ct, string billingFrequency = "weekly")
    {
        var customer = await _portfolio.GetCustomerByCprCvrAsync("8888888888", ct)
            ?? await _portfolio.CreateCustomerAsync("Period Boundary Test", "8888888888", "private", null, ct);
        var product = await _portfolio.CreateProductAsync("Spot Boundary", "spot", 4.0m, null, 39.00m, ct);

        var mp = new MeteringPoint(Gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        try { await _portfolio.CreateMeteringPointAsync(mp, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or PostgresException) { }

        try { await _portfolio.CreateContractAsync(customer.Id, Gsrn, product.Id, billingFrequency, "post_payment", effectiveDate, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or PostgresException) { }

        return (customer, product);
    }

    private async Task CreateCompletedProcessAsync(DateOnly effectiveDate, TestClock clock, CancellationToken ct)
    {
        var sm = new ProcessStateMachine(_processRepo, clock);
        var process = await sm.CreateRequestAsync(Gsrn, "supplier_switch", effectiveDate, ct);
        await sm.MarkSentAsync(process.Id, "corr-boundary-test", ct);
        await sm.MarkAcknowledgedAsync(process.Id, ct);
        await sm.MarkCompletedAsync(process.Id, ct);
    }

    private async Task ActivateAndSeedMeteringAsync(DateOnly periodStart, DateOnly exclusiveEnd, CancellationToken ct)
    {
        await _portfolio.ActivateMeteringPointAsync(Gsrn, periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct);

        try { await _portfolio.CreateSupplyPeriodAsync(Gsrn, periodStart, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or PostgresException) { }

        // exclusiveEnd is already the day after the last day, use it directly
        var rows = new List<MeteringDataRow>();
        var start = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = exclusiveEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
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
            rows.Add(new MeteringDataRow(ts, "PT1H", kwh, "A03", "test-boundary"));
        }

        await _meteringRepo.StoreTimeSeriesAsync(Gsrn, rows, ct);
    }

    private async Task RunOrchestrationAsync(TestClock clock, CancellationToken ct)
    {
        var completenessChecker = new MeteringCompletenessChecker(TestDatabase.ConnectionString);
        var dataLoader = new SettlementDataLoader(_meteringRepo, _spotPriceRepo, _tariffRepo);
        var engine = new SettlementEngine();
        var resultStore = new SettlementResultStore(TestDatabase.ConnectionString);

        var orchestration = new SettlementOrchestrationService(
            _processRepo, _portfolio,
            completenessChecker, dataLoader, engine, resultStore,
            clock,
            NullLogger<SettlementOrchestrationService>.Instance);

        await orchestration.RunTickAsync(ct);
    }
}
