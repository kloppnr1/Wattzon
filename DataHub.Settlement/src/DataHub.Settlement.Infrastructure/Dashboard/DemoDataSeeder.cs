using Dapper;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.Infrastructure.Settlement;
using DataHub.Settlement.Infrastructure.Tariff;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Dashboard;

public sealed class DemoDataSeeder
{
    private const string Gsrn = "571313100000012345";
    private readonly string _connectionString;

    static DemoDataSeeder()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public DemoDataSeeder(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> SeedAsync()
    {
        // Guard: skip if already seeded
        await using var checkConn = new NpgsqlConnection(_connectionString);
        await checkConn.OpenAsync();
        var exists = await checkConn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.customer LIMIT 1)");
        if (exists) return false;

        var ct = CancellationToken.None;

        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var processRepo = new ProcessRepository(_connectionString);

        // ── 1. Grid area ──
        await portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);

        // ── 2. Tariffs ──
        var gridRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
        {
            >= 1 and <= 6 => 0.06m,
            >= 7 and <= 16 => 0.18m,
            >= 17 and <= 20 => 0.54m,
            _ => 0.06m,
        })).ToList();
        await tariffRepo.SeedGridTariffAsync("344", "grid", new DateOnly(2025, 1, 1), gridRates, ct);
        await tariffRepo.SeedSubscriptionAsync("344", "grid", 49.00m, new DateOnly(2025, 1, 1), ct);
        await tariffRepo.SeedElectricityTaxAsync(0.008m, new DateOnly(2025, 1, 1), ct);

        // ── 3. Spot prices (744 hours, Jan 2025) ──
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
        await spotPriceRepo.StorePricesAsync(prices, ct);

        // ── 4. Customer + Product + Metering Point + Contract ──
        var customer = await portfolio.CreateCustomerAsync("Test Kunde", "0101901234", "private", ct);
        var product = await portfolio.CreateProductAsync("Spot Standard", "spot", 4.0m, null, 39.00m, ct);

        var mp = new MeteringPoint(Gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await portfolio.CreateMeteringPointAsync(mp, ct);
        await portfolio.ActivateMeteringPointAsync(Gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);

        var contract = await portfolio.CreateContractAsync(
            customer.Id, Gsrn, product.Id, "monthly", "post_payment", new DateOnly(2025, 1, 1), ct);
        await portfolio.CreateSupplyPeriodAsync(Gsrn, new DateOnly(2025, 1, 1), ct);

        // ── 5. Metering data (744 hourly readings, 0.55 kWh each = 409.200 kWh) ──
        var rows = new List<MeteringDataRow>();
        for (var i = 0; i < 744; i++)
        {
            var ts = start.AddHours(i);
            rows.Add(new MeteringDataRow(ts, "PT1H", 0.55m, "A01", "seed-msg-001"));
        }
        await meteringRepo.StoreTimeSeriesAsync(Gsrn, rows, ct);

        // ── 6. Process lifecycle ──
        var stateMachine = new ProcessStateMachine(processRepo, new SystemClock());
        var processRequest = await stateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await stateMachine.MarkSentAsync(processRequest.Id, "corr-seed-001", ct);
        await stateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);
        await stateMachine.MarkCompletedAsync(processRequest.Id, ct);

        // ── 7. Inbound messages (so Messages page has data) ──
        await using var msgConn = new NpgsqlConnection(_connectionString);
        await msgConn.OpenAsync();
        await msgConn.ExecuteAsync("""
            INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
            VALUES ('msg-rsm007-seed', 'RSM-007', 'corr-seed-001', 'MasterData', 'processed', 1024)
            """);
        await msgConn.ExecuteAsync("""
            INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
            VALUES ('msg-rsm012-seed', 'RSM-012', NULL, 'Timeseries', 'processed', 52000)
            """);
        await msgConn.ExecuteAsync("""
            INSERT INTO datahub.processed_message_id (message_id) VALUES ('msg-rsm007-seed')
            ON CONFLICT DO NOTHING
            """);
        await msgConn.ExecuteAsync("""
            INSERT INTO datahub.processed_message_id (message_id) VALUES ('msg-rsm012-seed')
            ON CONFLICT DO NOTHING
            """);

        // ── 8. Settlement run + lines (golden master result) ──
        var consumption = await meteringRepo.GetConsumptionAsync(Gsrn,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        var spotPricesForCalc = await spotPriceRepo.GetPricesAsync("DK1",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        var ratesForCalc = await tariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var electricityTax = await tariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct);
        var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct);

        var engine = new SettlementEngine();
        var result = engine.Calculate(new SettlementRequest(
            Gsrn,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            consumption, spotPricesForCalc,
            ratesForCalc, 0.054m, 0.049m, electricityTax,
            gridSub,
            product.MarginOrePerKwh / 100m,
            (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth));

        await using var settConn = new NpgsqlConnection(_connectionString);
        await settConn.OpenAsync();

        var billingPeriodId = await settConn.QuerySingleAsync<Guid>("""
            INSERT INTO settlement.billing_period (period_start, period_end, frequency)
            VALUES (@PeriodStart, @PeriodEnd, 'monthly')
            ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
            RETURNING id
            """, new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd });

        var settlementRunId = await settConn.QuerySingleAsync<Guid>("""
            INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, version, status, metering_points_count)
            VALUES (@BillingPeriodId, '344', 1, 'completed', 1)
            RETURNING id
            """, new { BillingPeriodId = billingPeriodId });

        foreach (var line in result.Lines)
        {
            await settConn.ExecuteAsync("""
                INSERT INTO settlement.settlement_line (settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency)
                VALUES (@RunId, @Gsrn, @ChargeType, @TotalKwh, @TotalAmount, @VatAmount, 'DKK')
                """, new
            {
                RunId = settlementRunId,
                Gsrn,
                line.ChargeType,
                TotalKwh = line.Kwh ?? 0m,
                TotalAmount = line.Amount,
                VatAmount = Math.Round(line.Amount * 0.25m, 2),
            });
        }

        return true;
    }
}
