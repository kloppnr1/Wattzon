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

public record SimulationStep(int Number, string Name, string Details);

public sealed class SimulationService
{
    private const string Gsrn = "571313100000012345";
    private readonly string _connectionString;

    static SimulationService()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public SimulationService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> IsAlreadySeededAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.customer LIMIT 1)");
    }

    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            TRUNCATE
                settlement.settlement_line,
                settlement.settlement_run,
                settlement.billing_period,
                lifecycle.process_event,
                lifecycle.process_request,
                datahub.dead_letter,
                datahub.processed_message_id,
                datahub.inbound_message,
                datahub.outbound_request,
                tariff.tariff_rate,
                tariff.grid_tariff,
                tariff.subscription,
                tariff.electricity_tax,
                portfolio.contract,
                portfolio.supply_period,
                portfolio.metering_point,
                portfolio.product,
                portfolio.customer,
                metering.metering_data,
                metering.spot_price
            CASCADE;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RunAsync(Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var processRepo = new ProcessRepository(_connectionString);

        // ── Step 1: Seed Reference Data ──
        await portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);

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

        var prices = new List<SpotPriceRow>();
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 744; i++)
        {
            var hour = start.AddHours(i);
            var price = hour.Hour switch
            {
                >= 0 and <= 5 => 0.45m,
                >= 6 and <= 15 => 0.85m,
                >= 16 and <= 19 => 1.25m,
                _ => 0.55m,
            };
            prices.Add(new SpotPriceRow("DK1", hour, price));
        }
        await spotPriceRepo.StorePricesAsync(prices, ct);

        await onStepCompleted(new SimulationStep(1, "Seed Reference Data",
            "Seeded grid area 344, 24 tariff rates, 744 spot prices, electricity tax"));
        await Task.Delay(1200, ct);

        // ── Step 2: Create Customer & Product ──
        var customer = await portfolio.CreateCustomerAsync("Test Kunde", "0101901234", "private", ct);
        var product = await portfolio.CreateProductAsync("Spot Standard", "spot", 4.0m, null, 39.00m, ct);

        await onStepCompleted(new SimulationStep(2, "Create Customer & Product",
            $"Customer '{customer.Name}' and product '{product.Name}' created"));
        await Task.Delay(800, ct);

        // ── Step 3: Create Metering Point ──
        var mp = new MeteringPoint(Gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await portfolio.CreateMeteringPointAsync(mp, ct);
        await portfolio.CreateContractAsync(
            customer.Id, Gsrn, product.Id, "monthly", "post_payment", new DateOnly(2025, 1, 1), ct);
        await portfolio.CreateSupplyPeriodAsync(Gsrn, new DateOnly(2025, 1, 1), ct);

        await onStepCompleted(new SimulationStep(3, "Create Metering Point",
            $"GSRN {Gsrn} with contract and supply period from 2025-01-01"));
        await Task.Delay(1000, ct);

        // ── Step 4: Submit BRS-001 ──
        var stateMachine = new ProcessStateMachine(processRepo);
        var processRequest = await stateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await stateMachine.MarkSentAsync(processRequest.Id, "corr-sim-001", ct);

        await onStepCompleted(new SimulationStep(4, "Submit BRS-001",
            $"Process {processRequest.Id:N} created and sent to DataHub"));
        await Task.Delay(2500, ct); // DataHub round-trip

        // ── Step 5: DataHub Acknowledges ──
        await stateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(5, "DataHub Acknowledges",
            "Process acknowledged and moved to effectuation_pending"));
        await Task.Delay(2000, ct); // Waiting for effectuation message

        // ── Step 6: Receive RSM-007 ──
        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES ('msg-rsm007-sim', 'RSM-007', 'corr-sim-001', 'MasterData', 'processed', 1024)
                """);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES ('msg-rsm007-sim')
                """);
        }
        await portfolio.ActivateMeteringPointAsync(Gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);

        await onStepCompleted(new SimulationStep(6, "Receive RSM-007",
            "Inbound RSM-007 recorded, metering point activated"));
        await Task.Delay(1000, ct);

        // ── Step 7: Complete Process ──
        await stateMachine.MarkCompletedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(7, "Complete Process",
            "Supplier switch process completed"));
        await Task.Delay(3000, ct); // Waiting for time series from DataHub

        // ── Step 8: Receive RSM-012 ──
        var rows = new List<MeteringDataRow>();
        for (var i = 0; i < 744; i++)
        {
            var ts = start.AddHours(i);
            rows.Add(new MeteringDataRow(ts, "PT1H", 0.55m, "A01", "msg-rsm012-sim"));
        }
        await meteringRepo.StoreTimeSeriesAsync(Gsrn, rows, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES ('msg-rsm012-sim', 'RSM-012', NULL, 'Timeseries', 'processed', 52000)
                """);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES ('msg-rsm012-sim')
                """);
        }

        await onStepCompleted(new SimulationStep(8, "Receive RSM-012",
            "744 hourly readings stored (409.200 kWh), inbound message recorded"));
        await Task.Delay(1500, ct);

        // ── Step 9: Run Settlement ──
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

        await using (var settConn = new NpgsqlConnection(_connectionString))
        {
            await settConn.OpenAsync(ct);

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
        }

        await onStepCompleted(new SimulationStep(9, "Run Settlement",
            $"Settlement complete — subtotal {result.Subtotal:N2} DKK, VAT {result.VatAmount:N2} DKK, total {result.Total:N2} DKK"));
    }
}
