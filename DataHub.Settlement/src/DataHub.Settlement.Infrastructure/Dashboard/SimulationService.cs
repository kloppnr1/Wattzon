using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Billing;
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
            CREATE SCHEMA IF NOT EXISTS billing;
            CREATE TABLE IF NOT EXISTS billing.aconto_payment (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                gsrn TEXT NOT NULL,
                period_start DATE NOT NULL,
                period_end DATE NOT NULL,
                amount NUMERIC(12,2) NOT NULL,
                paid_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            TRUNCATE
                billing.aconto_payment,
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

    // ── Common Setup ──────────────────────────────────────────────────

    private record CommonSetup(
        PortfolioRepository Portfolio,
        TariffRepository TariffRepo,
        SpotPriceRepository SpotPriceRepo,
        MeteringDataRepository MeteringRepo,
        ProcessRepository ProcessRepo,
        ProcessStateMachine StateMachine,
        Customer Customer,
        Product Product);

    private async Task<CommonSetup> SeedCommonDataAsync(
        Func<SimulationStep, Task> onStepCompleted, CancellationToken ct,
        string customerName = "Test Kunde", bool createSupplyPeriod = true)
    {
        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var processRepo = new ProcessRepository(_connectionString);
        var stateMachine = new ProcessStateMachine(processRepo);

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
                >= 0 and <= 5 => 45m,
                >= 6 and <= 15 => 85m,
                >= 16 and <= 19 => 125m,
                _ => 55m,
            };
            prices.Add(new SpotPriceRow("DK1", hour, price));
        }
        await spotPriceRepo.StorePricesAsync(prices, ct);

        await onStepCompleted(new SimulationStep(1, "Seed Reference Data",
            "Seeded grid area 344, 24 tariff rates, 744 spot prices, electricity tax"));
        await Task.Delay(1200, ct);

        // ── Step 2: Create Customer & Product ──
        var customer = await portfolio.CreateCustomerAsync(customerName, "0101901234", "private", ct);
        var product = await portfolio.CreateProductAsync("Spot Standard", "spot", 4.0m, null, 39.00m, ct);

        await onStepCompleted(new SimulationStep(2, "Create Customer & Product",
            $"Customer '{customer.Name}' and product '{product.Name}' created"));
        await Task.Delay(800, ct);

        // ── Step 3: Create Metering Point ──
        var mp = new MeteringPoint(Gsrn, "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await portfolio.CreateMeteringPointAsync(mp, ct);
        await portfolio.CreateContractAsync(
            customer.Id, Gsrn, product.Id, "monthly", "post_payment", new DateOnly(2025, 1, 1), ct);

        if (createSupplyPeriod)
        {
            await portfolio.CreateSupplyPeriodAsync(Gsrn, new DateOnly(2025, 1, 1), ct);
            await onStepCompleted(new SimulationStep(3, "Create Metering Point",
                $"GSRN {Gsrn} with contract and supply period from 2025-01-01"));
        }
        else
        {
            await onStepCompleted(new SimulationStep(3, "Create Metering Point",
                $"GSRN {Gsrn} with contract (no supply period yet)"));
        }
        await Task.Delay(1000, ct);

        return new CommonSetup(portfolio, tariffRepo, spotPriceRepo, meteringRepo,
            processRepo, stateMachine, customer, product);
    }

    // ── Scenario 1: Sunshine (existing) ──────────────────────────────

    public async Task RunSunshineAsync(Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var setup = await SeedCommonDataAsync(onStepCompleted, ct);
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // ── Step 4: Submit BRS-001 ──
        var processRequest = await setup.StateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await setup.StateMachine.MarkSentAsync(processRequest.Id, "corr-sim-001", ct);

        await onStepCompleted(new SimulationStep(4, "Submit BRS-001",
            $"Process {processRequest.Id:N} created and sent to DataHub"));
        await Task.Delay(2500, ct);

        // ── Step 5: DataHub Acknowledges ──
        await setup.StateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(5, "DataHub Acknowledges",
            "Process acknowledged and moved to effectuation_pending"));
        await Task.Delay(2000, ct);

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
        await setup.Portfolio.ActivateMeteringPointAsync(Gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);

        await onStepCompleted(new SimulationStep(6, "Receive RSM-007",
            "Inbound RSM-007 recorded, metering point activated"));
        await Task.Delay(1000, ct);

        // ── Step 7: Complete Process ──
        await setup.StateMachine.MarkCompletedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(7, "Complete Process",
            "Supplier switch process completed"));
        await Task.Delay(3000, ct);

        // ── Step 8: Receive RSM-012 ──
        // In production, RSM-012 arrives once per day (~02:00), covering the previous 24 hours.
        // Here we simulate 31 daily deliveries as a single batch for January.
        var rows = new List<MeteringDataRow>();
        for (var i = 0; i < 744; i++)
        {
            var ts = start.AddHours(i);
            rows.Add(new MeteringDataRow(ts, "PT1H", 0.55m, "A01", "msg-rsm012-sim"));
        }
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, rows, ct);

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
            "31 daily RSM-012 deliveries — 744 hourly readings (409.200 kWh)"));
        await Task.Delay(1500, ct);

        // ── Step 9: Run Settlement ──
        var result = await RunSettlementAsync(setup, start, start.AddMonths(1), ct);

        await onStepCompleted(new SimulationStep(9, "Run Settlement",
            $"Settlement complete — subtotal {result.Subtotal:N2} DKK, VAT {result.VatAmount:N2} DKK, total {result.Total:N2} DKK"));
        await Task.Delay(1500, ct);

        // ── Step 10: Incoming BRS-001 (Another Supplier) ──
        await setup.StateMachine.MarkOffboardingAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(10, "Incoming BRS-001",
            "Another supplier requested the metering point — offboarding started"));
        await Task.Delay(2000, ct);

        // ── Step 11: Receive Final RSM-012 ──
        var finalStart = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var finalRows = new List<MeteringDataRow>();
        for (var i = 0; i < 360; i++)
        {
            var ts = finalStart.AddHours(i);
            finalRows.Add(new MeteringDataRow(ts, "PT1H", 0.55m, "A01", "msg-rsm012-final-sim"));
        }
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, finalRows, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES ('msg-rsm012-final-sim', 'RSM-012', NULL, 'Timeseries', 'processed', 25000)
                """);
        }

        await onStepCompleted(new SimulationStep(11, "Receive Final RSM-012",
            "360 hourly readings (Feb 1-16), final metering data before departure"));
        await Task.Delay(1500, ct);

        // ── Step 12: Run Final Settlement ──
        var febPrices = GenerateSpotPrices("DK2", finalStart, 360);
        await setup.SpotPriceRepo.StorePricesAsync(febPrices, ct);

        var finalConsumption = await setup.MeteringRepo.GetConsumptionAsync(Gsrn,
            finalStart, new DateTime(2025, 2, 16, 0, 0, 0, DateTimeKind.Utc), ct);
        var finalSpotPrices = await setup.SpotPriceRepo.GetPricesAsync("DK2",
            finalStart, new DateTime(2025, 2, 16, 0, 0, 0, DateTimeKind.Utc), ct);
        var ratesForCalc = await setup.TariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var electricityTax = await setup.TariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct);
        var gridSub = await setup.TariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct);

        var finalEngine = new SettlementEngine();
        var finalService = new FinalSettlementService(finalEngine);
        var finalRequest = new SettlementRequest(
            Gsrn,
            new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 16),
            finalConsumption, finalSpotPrices,
            ratesForCalc, 0.054m, 0.049m, electricityTax,
            gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth);

        var finalResult = finalService.CalculateFinal(finalRequest, acontoPaid: null);

        await setup.Portfolio.EndSupplyPeriodAsync(Gsrn, new DateOnly(2025, 2, 16), "supplier_switch", ct);
        await setup.Portfolio.EndContractAsync(Gsrn, new DateOnly(2025, 2, 16), ct);
        await setup.Portfolio.DeactivateMeteringPointAsync(Gsrn,
            new DateTime(2025, 2, 16, 0, 0, 0, DateTimeKind.Utc), ct);
        await setup.StateMachine.MarkFinalSettledAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(12, "Run Final Settlement",
            $"Final settlement — total {finalResult.TotalDue:N2} DKK, customer offboarded"));
    }

    // ── Scenario 2: Rejection & Retry ────────────────────────────────

    public async Task RunRejectionRetryAsync(Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var setup = await SeedCommonDataAsync(onStepCompleted, ct, createSupplyPeriod: false);

        // ── Step 4: Submit BRS-001 ──
        var processRequest = await setup.StateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await setup.StateMachine.MarkSentAsync(processRequest.Id, "corr-rej-001", ct);

        await onStepCompleted(new SimulationStep(4, "Submit BRS-001",
            $"Process {processRequest.Id:N} sent (correlation: corr-rej-001)"));
        await Task.Delay(2500, ct);

        // ── Step 5: DataHub Rejects ──
        await setup.StateMachine.MarkRejectedAsync(processRequest.Id, "E16: Customer not found at metering point", ct);

        await onStepCompleted(new SimulationStep(5, "DataHub Rejects",
            "E16: Customer not found at metering point"));
        await Task.Delay(2000, ct);

        // ── Step 6: Retry with New BRS-001 ──
        var retryRequest = await setup.StateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await setup.StateMachine.MarkSentAsync(retryRequest.Id, "corr-rej-002", ct);

        await onStepCompleted(new SimulationStep(6, "Retry with New BRS-001",
            $"New process {retryRequest.Id:N} sent (correlation: corr-rej-002)"));
        await Task.Delay(2500, ct);

        // ── Step 7: DataHub Acknowledges Retry ──
        await setup.StateMachine.MarkAcknowledgedAsync(retryRequest.Id, ct);

        await onStepCompleted(new SimulationStep(7, "DataHub Acknowledges Retry",
            "Retry acknowledged, moved to effectuation_pending"));
        await Task.Delay(2000, ct);

        // ── Step 8: Process Completes ──
        await setup.StateMachine.MarkCompletedAsync(retryRequest.Id, ct);
        await setup.Portfolio.ActivateMeteringPointAsync(Gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        await setup.Portfolio.CreateSupplyPeriodAsync(Gsrn, new DateOnly(2025, 1, 1), ct);

        await onStepCompleted(new SimulationStep(8, "Process Completes",
            "Retry successful! Metering point activated, supply period created"));
    }

    // ── Scenario 3: Aconto Quarterly Billing ─────────────────────────

    public async Task RunAcontoQuarterlyAsync(Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var setup = await SeedCommonDataAsync(onStepCompleted, ct, customerName: "Aconto Kunde");
        var acontoRepo = new AcontoPaymentRepository(_connectionString);
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // ── Step 4 (3+1): Activate via abbreviated BRS-001 ──
        var processRequest = await setup.StateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await setup.StateMachine.MarkSentAsync(processRequest.Id, "corr-aconto-001", ct);
        await setup.StateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);
        await setup.StateMachine.MarkCompletedAsync(processRequest.Id, ct);
        await setup.Portfolio.ActivateMeteringPointAsync(Gsrn, start, ct);

        await onStepCompleted(new SimulationStep(4, "Activate Metering Point",
            "BRS-001 sent, acknowledged, completed — supply active from 2025-01-01"));
        await Task.Delay(1500, ct);

        // ── Step 5 (4): Estimate Q1 Aconto ──
        var expectedPrice = AcontoEstimator.CalculateExpectedPricePerKwh(
            averageSpotPriceOrePerKwh: 75m, marginOrePerKwh: 4.0m,
            systemTariffRate: 0.054m, transmissionTariffRate: 0.049m,
            electricityTaxRate: 0.008m, averageGridTariffRate: 0.18m);
        var gridSubRate = 49.00m;
        var supplierSubRate = setup.Product.SubscriptionKrPerMonth;
        var quarterlyEstimate = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: 4000m, expectedPrice, gridSubRate, supplierSubRate);

        // Customer pays the full quarterly aconto amount upfront at Q1 start
        await acontoRepo.RecordPaymentAsync(Gsrn, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), quarterlyEstimate, ct);

        await onStepCompleted(new SimulationStep(5, "Estimate Q1 Aconto",
            $"Quarterly estimate: {quarterlyEstimate:N2} DKK (paid upfront for Q1)"));
        await Task.Delay(1200, ct);

        // ── Step 6: Receive January Metering ──
        // In production, RSM-012 arrives once per day (~02:00), covering the previous 24 hours.
        // Here we simulate 31 daily deliveries as a single batch.
        var janRows = GenerateMeteringData(start, 744, 0.55m, "msg-aconto-jan");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, janRows, ct);

        await onStepCompleted(new SimulationStep(6, "Receive January Metering",
            $"31 daily RSM-012 deliveries — 744 hourly readings ({744 * 0.55m:N1} kWh total)"));
        await Task.Delay(1000, ct);

        // ── Step 7: Receive February Metering ──
        var febStart = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var febRows = GenerateMeteringData(febStart, 672, 0.55m, "msg-aconto-feb");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, febRows, ct);

        var febPrices = GenerateSpotPrices("DK1", febStart, 672);
        await setup.SpotPriceRepo.StorePricesAsync(febPrices, ct);

        await onStepCompleted(new SimulationStep(7, "Receive February Metering",
            $"28 daily RSM-012 deliveries — 672 hourly readings ({672 * 0.55m:N1} kWh total)"));
        await Task.Delay(1000, ct);

        // ── Step 8: Receive March Metering ──
        var marStart = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var marRows = GenerateMeteringData(marStart, 744, 0.55m, "msg-aconto-mar");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, marRows, ct);

        var marPrices = GenerateSpotPrices("DK1", marStart, 744);
        await setup.SpotPriceRepo.StorePricesAsync(marPrices, ct);

        await onStepCompleted(new SimulationStep(8, "Receive March Metering",
            $"31 daily RSM-012 deliveries — 744 hourly readings ({744 * 0.55m:N1} kWh total)"));
        await Task.Delay(1000, ct);

        // ── Step 9: Run Q1 Settlement ──
        var q1End = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var q1Consumption = await setup.MeteringRepo.GetConsumptionAsync(Gsrn, start, q1End, ct);
        var q1SpotPrices = await setup.SpotPriceRepo.GetPricesAsync("DK1", start, q1End, ct);
        var rates = await setup.TariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await setup.TariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct);
        var gridSub = await setup.TariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct);

        // Calculate each month separately and sum (subscriptions are per-month)
        var engine = new SettlementEngine();
        var janResult = engine.Calculate(new SettlementRequest(Gsrn,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            q1Consumption.Where(r => r.Timestamp < febStart).ToList(),
            q1SpotPrices.Where(p => p.Hour < febStart).ToList(),
            rates, 0.054m, 0.049m, elTax, gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth));
        var febResult = engine.Calculate(new SettlementRequest(Gsrn,
            new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 1),
            q1Consumption.Where(r => r.Timestamp >= febStart && r.Timestamp < marStart).ToList(),
            q1SpotPrices.Where(p => p.Hour >= febStart && p.Hour < marStart).ToList(),
            rates, 0.054m, 0.049m, elTax, gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth));
        var marResult = engine.Calculate(new SettlementRequest(Gsrn,
            new DateOnly(2025, 3, 1), new DateOnly(2025, 4, 1),
            q1Consumption.Where(r => r.Timestamp >= marStart).ToList(),
            q1SpotPrices.Where(p => p.Hour >= marStart).ToList(),
            rates, 0.054m, 0.049m, elTax, gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth));

        var q1Total = janResult.Total + febResult.Total + marResult.Total;

        await onStepCompleted(new SimulationStep(9, "Run Q1 Settlement",
            $"Q1 actual: {q1Total:N2} DKK (Jan {janResult.Total:N2} + Feb {febResult.Total:N2} + Mar {marResult.Total:N2})"));
        await Task.Delay(1500, ct);

        // ── Step 10: Aconto Reconciliation ──
        var difference = q1Total - quarterlyEstimate;

        await onStepCompleted(new SimulationStep(10, "Aconto Reconciliation",
            $"Aconto paid {quarterlyEstimate:N2} DKK, actual {q1Total:N2} DKK, difference {difference:N2} DKK"));
        await Task.Delay(1200, ct);

        // ── Step 11: Estimate Q2 + Combined Invoice ──
        var q1TotalKwh = janResult.TotalKwh + febResult.TotalKwh + marResult.TotalKwh;
        var q2Estimate = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: q1TotalKwh * 4m, expectedPrice, gridSubRate, supplierSubRate);
        var totalDue = difference + q2Estimate;

        await onStepCompleted(new SimulationStep(11, "Combined Invoice",
            $"Difference {difference:N2} + Q2 aconto {q2Estimate:N2} = total due {totalDue:N2} DKK"));
    }

    // ── Scenario 4: Cancellation ─────────────────────────────────────

    public async Task RunCancellationAsync(Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var setup = await SeedCommonDataAsync(onStepCompleted, ct, createSupplyPeriod: false);

        // ── Step 4: Submit BRS-001 ──
        var processRequest = await setup.StateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await setup.StateMachine.MarkSentAsync(processRequest.Id, "corr-cancel-001", ct);

        await onStepCompleted(new SimulationStep(4, "Submit BRS-001",
            $"Process {processRequest.Id:N} sent (correlation: corr-cancel-001)"));
        await Task.Delay(2500, ct);

        // ── Step 5: DataHub Acknowledges ──
        await setup.StateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(5, "DataHub Acknowledges",
            "Process acknowledged, moved to effectuation_pending"));
        await Task.Delay(2000, ct);

        // ── Step 6: Cancel Before Effectuation ──
        await setup.StateMachine.MarkCancelledAsync(processRequest.Id, "Customer changed their mind", ct);

        await onStepCompleted(new SimulationStep(6, "Cancel Before Effectuation",
            "BRS-003 sent, process cancelled before effectuation"));
        await Task.Delay(2000, ct);

        // ── Step 7: Verify Clean State ──
        var supplyPeriods = await setup.Portfolio.GetSupplyPeriodsAsync(Gsrn, ct);
        var process = await setup.ProcessRepo.GetAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(7, "Verify Clean State",
            $"No supply activated ({supplyPeriods.Count} periods), process status: {process?.Status ?? "unknown"}"));
    }

    // ── Operations: Change of Supplier (concurrent-safe) ───────────

    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public async Task RunChangeOfSupplierAsync(
        string gsrn, string customerName,
        Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var processRepo = new ProcessRepository(_connectionString);
        var stateMachine = new ProcessStateMachine(processRepo);

        // ── Step 1: Seed Reference Data (idempotent, serialized) ──
        await _seedLock.WaitAsync(ct);
        try
        {
            await portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);

            var gridRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
            {
                >= 1 and <= 6 => 0.06m,
                >= 7 and <= 16 => 0.18m,
                >= 17 and <= 20 => 0.54m,
                _ => 0.06m,
            })).ToList();
            await tariffRepo.SeedGridTariffAsync("344", "grid", new DateOnly(2025, 1, 1), gridRates, ct);

            // Subscription has no unique constraint — check before inserting
            await using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                var subExists = await conn.QuerySingleAsync<bool>(
                    "SELECT EXISTS(SELECT 1 FROM tariff.subscription WHERE grid_area_code = '344' AND subscription_type = 'grid')");
                if (!subExists)
                    await tariffRepo.SeedSubscriptionAsync("344", "grid", 49.00m, new DateOnly(2025, 1, 1), ct);
            }

            await tariffRepo.SeedElectricityTaxAsync(0.008m, new DateOnly(2025, 1, 1), ct);

            var prices = new List<SpotPriceRow>();
            var priceStart = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 744; i++)
            {
                var hour = priceStart.AddHours(i);
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
        }
        finally
        {
            _seedLock.Release();
        }

        await onStepCompleted(new SimulationStep(1, "Seed Reference Data",
            "Grid area 344, tariffs, spot prices ready"));
        await Task.Delay(800, ct);

        // ── Step 2: Create Customer & Metering Point ──
        var customer = await portfolio.CreateCustomerAsync(customerName, "0101901234", "private", ct);
        var product = await portfolio.CreateProductAsync($"Spot-{gsrn[^4..]}", "spot", 4.0m, null, 39.00m, ct);

        // All per-GSRN inserts are idempotent — GSRN counter resets on app restart but DB persists
        await using (var setupConn = new NpgsqlConnection(_connectionString))
        {
            await setupConn.OpenAsync(ct);
            await setupConn.ExecuteAsync("""
                INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, grid_area_code, grid_operator_gln, price_area)
                VALUES (@Gsrn, 'E17', 'flex', '344', '5790000392261', 'DK1')
                ON CONFLICT (gsrn) DO NOTHING
                """, new { Gsrn = gsrn });
            await setupConn.ExecuteAsync("""
                INSERT INTO portfolio.contract (customer_id, gsrn, product_id, billing_frequency, payment_model, start_date)
                VALUES (@CustomerId, @Gsrn, @ProductId, 'monthly', 'post_payment', @StartDate)
                ON CONFLICT (gsrn, start_date) DO NOTHING
                """, new { CustomerId = customer.Id, Gsrn = gsrn, ProductId = product.Id, StartDate = new DateOnly(2025, 1, 1) });
            await setupConn.ExecuteAsync("""
                INSERT INTO portfolio.supply_period (gsrn, start_date)
                SELECT @Gsrn, @StartDate
                WHERE NOT EXISTS (SELECT 1 FROM portfolio.supply_period WHERE gsrn = @Gsrn AND start_date = @StartDate)
                """, new { Gsrn = gsrn, StartDate = new DateOnly(2025, 1, 1) });
        }

        await onStepCompleted(new SimulationStep(2, "Create Customer & Metering Point",
            $"{customerName}, GSRN {gsrn}, contract + supply from 2025-01-01"));
        await Task.Delay(800, ct);

        // ── Step 3: Submit BRS-001 ──
        var uid = Guid.NewGuid().ToString("N")[..8];
        var corrId = $"corr-ops-{uid}";
        var processRequest = await stateMachine.CreateRequestAsync(gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await stateMachine.MarkSentAsync(processRequest.Id, corrId, ct);

        await onStepCompleted(new SimulationStep(3, "Submit BRS-001",
            $"Process {processRequest.Id:N} sent"));
        await Task.Delay(1500, ct);

        // ── Step 4: DataHub Acknowledges ──
        await stateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(4, "DataHub Acknowledges",
            "Process acknowledged → effectuation_pending"));
        await Task.Delay(1200, ct);

        // ── Step 5: Receive RSM-007 ──
        var msgId007 = $"msg-rsm007-ops-{uid}";
        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES (@MsgId, 'RSM-007', @CorrId, 'MasterData', 'processed', 1024)
                """, new { MsgId = msgId007, CorrId = corrId });
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                """, new { MsgId = msgId007 });
        }
        await portfolio.ActivateMeteringPointAsync(gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);

        await onStepCompleted(new SimulationStep(5, "Receive RSM-007",
            "Metering point activated"));
        await Task.Delay(1000, ct);

        // ── Step 6: Complete Process ──
        await stateMachine.MarkCompletedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(6, "Complete Process",
            "Supplier switch completed"));
        await Task.Delay(1500, ct);

        // ── Step 7: Receive RSM-012 ──
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var msgId012 = $"msg-rsm012-ops-{uid}";
        var rows = GenerateMeteringData(start, 744, 0.55m, msgId012);
        await meteringRepo.StoreTimeSeriesAsync(gsrn, rows, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES (@MsgId, 'RSM-012', NULL, 'Timeseries', 'processed', 52000)
                """, new { MsgId = msgId012 });
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                """, new { MsgId = msgId012 });
        }

        await onStepCompleted(new SimulationStep(7, "Receive RSM-012",
            "744 hourly readings (409.200 kWh)"));
        await Task.Delay(1000, ct);

        // ── Step 8: Run Settlement ──
        var periodEnd = start.AddMonths(1);
        var consumption = await meteringRepo.GetConsumptionAsync(gsrn, start, periodEnd, ct);
        var spotPrices = await spotPriceRepo.GetPricesAsync("DK1", start, periodEnd, ct);
        var rates = await tariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await tariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct);
        var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct);

        var engine = new SettlementEngine();
        var result = engine.Calculate(new SettlementRequest(
            gsrn,
            DateOnly.FromDateTime(start), DateOnly.FromDateTime(periodEnd),
            consumption, spotPrices,
            rates, 0.054m, 0.049m, elTax,
            gridSub,
            product.MarginOrePerKwh / 100m,
            (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth));

        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);

            // Advisory lock prevents concurrent MAX(version) race without serializable isolation
            await conn.ExecuteAsync("SELECT pg_advisory_lock(8675309)");
            try
            {
                var billingPeriodId = await conn.QuerySingleAsync<Guid>("""
                    INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                    VALUES (@PeriodStart, @PeriodEnd, 'monthly')
                    ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
                    RETURNING id
                    """, new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd });

                var settlementRunId = await conn.QuerySingleAsync<Guid>("""
                    INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, version, status, metering_points_count)
                    VALUES (
                        @BillingPeriodId, '344',
                        COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                  WHERE billing_period_id = @BillingPeriodId AND grid_area_code = '344'), 0) + 1,
                        'completed', 1)
                    RETURNING id
                    """, new { BillingPeriodId = billingPeriodId });

                foreach (var line in result.Lines)
                {
                    await conn.ExecuteAsync("""
                        INSERT INTO settlement.settlement_line (settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency)
                        VALUES (@RunId, @Gsrn, @ChargeType, @TotalKwh, @TotalAmount, @VatAmount, 'DKK')
                        """, new
                    {
                        RunId = settlementRunId,
                        Gsrn = gsrn,
                        line.ChargeType,
                        TotalKwh = line.Kwh ?? 0m,
                        TotalAmount = line.Amount,
                        VatAmount = Math.Round(line.Amount * 0.25m, 2),
                    });
                }
            }
            finally
            {
                await conn.ExecuteAsync("SELECT pg_advisory_unlock(8675309)");
            }
        }

        await onStepCompleted(new SimulationStep(8, "Run Settlement",
            $"Total: {result.Total:N2} DKK (subtotal {result.Subtotal:N2}, VAT {result.VatAmount:N2})"));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<SettlementResult> RunSettlementAsync(CommonSetup setup, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        var consumption = await setup.MeteringRepo.GetConsumptionAsync(Gsrn, periodStart, periodEnd, ct);
        var spotPrices = await setup.SpotPriceRepo.GetPricesAsync("DK1", periodStart, periodEnd, ct);
        var rates = await setup.TariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await setup.TariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct);
        var gridSub = await setup.TariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct);

        var engine = new SettlementEngine();
        var result = engine.Calculate(new SettlementRequest(
            Gsrn,
            DateOnly.FromDateTime(periodStart), DateOnly.FromDateTime(periodEnd),
            consumption, spotPrices,
            rates, 0.054m, 0.049m, elTax,
            gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth));

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var billingPeriodId = await conn.QuerySingleAsync<Guid>("""
            INSERT INTO settlement.billing_period (period_start, period_end, frequency)
            VALUES (@PeriodStart, @PeriodEnd, 'monthly')
            ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
            RETURNING id
            """, new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd });

        var settlementRunId = await conn.QuerySingleAsync<Guid>("""
            INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, version, status, metering_points_count)
            VALUES (@BillingPeriodId, '344', 1, 'completed', 1)
            RETURNING id
            """, new { BillingPeriodId = billingPeriodId });

        foreach (var line in result.Lines)
        {
            await conn.ExecuteAsync("""
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

        return result;
    }

    private static List<MeteringDataRow> GenerateMeteringData(DateTime start, int hours, decimal kwhPerHour, string sourceMessageId)
    {
        var rows = new List<MeteringDataRow>(hours);
        for (var i = 0; i < hours; i++)
        {
            rows.Add(new MeteringDataRow(start.AddHours(i), "PT1H", kwhPerHour, "A01", sourceMessageId));
        }
        return rows;
    }

    private static List<SpotPriceRow> GenerateSpotPrices(string priceArea, DateTime start, int hours)
    {
        var prices = new List<SpotPriceRow>(hours);
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
            prices.Add(new SpotPriceRow(priceArea, hour, price));
        }
        return prices;
    }
}
