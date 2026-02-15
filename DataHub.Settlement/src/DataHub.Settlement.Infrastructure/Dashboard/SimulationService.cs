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

public record SimulationStep(int Number, string Name, string Details, DateOnly? SimulatedDate = null);

public record MeteringPointSummary(
    string ConnectionStatus,
    DateTime? ActivatedAt,
    DateTime? DeactivatedAt,
    IReadOnlyList<MeteringPointSummary.ProcessInfo> Processes,
    IReadOnlyList<MeteringPointSummary.SupplyInfo> SupplyPeriods,
    MeteringPointSummary.MeteringInfo? Metering,
    IReadOnlyList<MeteringPointSummary.SettlementInfo> Settlements,
    IReadOnlyList<MeteringPointSummary.AcontoInfo> AcontoPayments)
{
    public record ProcessInfo(Guid Id, string ProcessType, string Status, DateOnly? EffectiveDate);
    public record SupplyInfo(DateOnly StartDate, DateOnly? EndDate, string? EndReason);
    public record MeteringInfo(DateTime FirstReading, DateTime LastReading, int ReadingCount, decimal TotalKwh);
    public record SettlementInfo(DateOnly PeriodStart, DateOnly PeriodEnd, decimal TotalAmount, decimal VatAmount, string Status);
    public record AcontoInfo(DateOnly PeriodStart, DateOnly PeriodEnd, decimal Amount);
}

public sealed class SimulationService
{
    private const string Gsrn = "571313100000012345";
    private readonly string _connectionString;
    private readonly Domain.IClock _clock;

    static SimulationService()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public SimulationService(string connectionString, Domain.IClock? clock = null)
    {
        _connectionString = connectionString;
        _clock = clock ?? new SystemClock();
    }

    public async Task<MeteringPointSummary?> GetMeteringPointSummaryAsync(string gsrn, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var mpRow = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT connection_status, activated_at, deactivated_at FROM portfolio.metering_point WHERE gsrn = @Gsrn",
            new { Gsrn = gsrn });
        if (mpRow is null)
            return null;

        string connectionStatus = mpRow.connection_status;
        DateTime? activatedAt = mpRow.activated_at;
        DateTime? deactivatedAt = mpRow.deactivated_at;

        var processRows = await conn.QueryAsync<dynamic>(
            "SELECT id, process_type, status, effective_date FROM lifecycle.process_request WHERE gsrn = @Gsrn ORDER BY created_at DESC LIMIT 1",
            new { Gsrn = gsrn });
        var processes = processRows.Select(r => new MeteringPointSummary.ProcessInfo(
            (Guid)r.id, (string)r.process_type, (string)r.status,
            r.effective_date is null ? null : DateOnly.FromDateTime((DateTime)r.effective_date))).ToList();

        var spRows = await conn.QueryAsync<dynamic>(
            "SELECT start_date, end_date, end_reason FROM portfolio.supply_period WHERE gsrn = @Gsrn ORDER BY start_date",
            new { Gsrn = gsrn });
        var supplyPeriods = spRows.Select(r => new MeteringPointSummary.SupplyInfo(
            DateOnly.FromDateTime((DateTime)r.start_date),
            r.end_date is null ? null : DateOnly.FromDateTime((DateTime)r.end_date),
            (string?)r.end_reason)).ToList();

        var mRow = await conn.QuerySingleAsync<dynamic>(
            "SELECT MIN(timestamp) AS first_ts, MAX(timestamp) AS last_ts, COUNT(*)::int AS cnt, COALESCE(SUM(quantity_kwh), 0) AS total FROM metering.metering_data WHERE metering_point_id = @Gsrn",
            new { Gsrn = gsrn });
        MeteringPointSummary.MeteringInfo? metering = mRow.first_ts is not null
            ? new MeteringPointSummary.MeteringInfo((DateTime)mRow.first_ts, (DateTime)mRow.last_ts, (int)mRow.cnt, (decimal)mRow.total)
            : null;

        var sRows = await conn.QueryAsync<dynamic>("""
            SELECT bp.period_start, bp.period_end,
                   COALESCE(SUM(sl.total_amount), 0) AS total_amount,
                   COALESCE(SUM(sl.vat_amount), 0) AS vat_amount,
                   sr.status
            FROM settlement.settlement_line sl
            JOIN settlement.settlement_run sr ON sr.id = sl.settlement_run_id
            JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
            WHERE sl.metering_point_id = @Gsrn
            GROUP BY bp.period_start, bp.period_end, sr.status
            ORDER BY bp.period_start
            """, new { Gsrn = gsrn });
        var settlements = sRows.Select(r => new MeteringPointSummary.SettlementInfo(
            DateOnly.FromDateTime((DateTime)r.period_start),
            DateOnly.FromDateTime((DateTime)r.period_end),
            (decimal)r.total_amount, (decimal)r.vat_amount, (string)r.status)).ToList();

        var aRows = await conn.QueryAsync<dynamic>(
            "SELECT period_start, period_end, amount FROM billing.aconto_payment WHERE gsrn = @Gsrn ORDER BY period_start",
            new { Gsrn = gsrn });
        var acontoPayments = aRows.Select(r => new MeteringPointSummary.AcontoInfo(
            DateOnly.FromDateTime((DateTime)r.period_start),
            DateOnly.FromDateTime((DateTime)r.period_end),
            (decimal)r.amount)).ToList();

        return new MeteringPointSummary(
            connectionStatus, activatedAt, deactivatedAt,
            processes, supplyPeriods, metering, settlements, acontoPayments);
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
        var stateMachine = new ProcessStateMachine(processRepo, _clock);

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
        // await Task.Delay(1200, ct);

        // ── Step 2: Create Customer & Product ──
        var customer = await portfolio.CreateCustomerAsync(customerName, "0101901234", "private", null, ct);
        var product = await portfolio.CreateProductAsync("Spot Standard", "spot", 4.0m, null, 39.00m, ct);

        await onStepCompleted(new SimulationStep(2, "Create Customer & Product",
            $"Customer '{customer.Name}' and product '{product.Name}' created"));
        // await Task.Delay(800, ct);

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
        // await Task.Delay(1000, ct);

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
        // await Task.Delay(2500, ct);

        // ── Step 5: DataHub Acknowledges ──
        await setup.StateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(5, "DataHub Acknowledges",
            "Process acknowledged and moved to effectuation_pending"));
        // await Task.Delay(2000, ct);

        // ── Step 6: Receive RSM-022 ──
        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES ('msg-rsm022-sim', 'RSM-022', 'corr-sim-001', 'MasterData', 'processed', 1024)
                """);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES ('msg-rsm022-sim')
                """);
        }
        await setup.Portfolio.ActivateMeteringPointAsync(Gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);

        await onStepCompleted(new SimulationStep(6, "Receive RSM-022",
            "Inbound RSM-022 recorded, metering point activated"));
        // await Task.Delay(1000, ct);

        // ── Step 7: Complete Process ──
        await setup.StateMachine.MarkCompletedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(7, "Complete Process",
            "Supplier switch process completed"));
        // await Task.Delay(3000, ct);

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
        // await Task.Delay(1500, ct);

        // ── Step 9: Run Settlement ──
        var result = await RunSettlementAsync(setup, start, start.AddMonths(1), ct);

        await onStepCompleted(new SimulationStep(9, "Run Settlement",
            $"Settlement complete — subtotal {result.Subtotal:N2} DKK, VAT {result.VatAmount:N2} DKK, total {result.Total:N2} DKK"));
        // await Task.Delay(1500, ct);

        // ── Step 10: Incoming BRS-001 (Another Supplier) ──
        await setup.StateMachine.MarkOffboardingAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(10, "Incoming BRS-001",
            "Another supplier requested the metering point — offboarding started"));
        // await Task.Delay(2000, ct);

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
        // await Task.Delay(1500, ct);

        // ── Step 12: Run Final Settlement ──
        var febPrices = GenerateSpotPrices("DK2", finalStart, 360);
        await setup.SpotPriceRepo.StorePricesAsync(febPrices, ct);

        var finalConsumption = await setup.MeteringRepo.GetConsumptionAsync(Gsrn,
            finalStart, new DateTime(2025, 2, 16, 0, 0, 0, DateTimeKind.Utc), ct);
        var finalSpotPrices = await setup.SpotPriceRepo.GetPricesAsync("DK2",
            finalStart, new DateTime(2025, 2, 16, 0, 0, 0, DateTimeKind.Utc), ct);
        var ratesForCalc = await setup.TariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var electricityTax = await setup.TariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct) ?? 0m;
        var gridSub = await setup.TariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct) ?? 0m;

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
        // await Task.Delay(2500, ct);

        // ── Step 5: DataHub Rejects ──
        await setup.StateMachine.MarkRejectedAsync(processRequest.Id, "E16: Customer not found at metering point", ct);

        await onStepCompleted(new SimulationStep(5, "DataHub Rejects",
            "E16: Customer not found at metering point"));
        // await Task.Delay(2000, ct);

        // ── Step 6: Retry with New BRS-001 ──
        var retryRequest = await setup.StateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await setup.StateMachine.MarkSentAsync(retryRequest.Id, "corr-rej-002", ct);

        await onStepCompleted(new SimulationStep(6, "Retry with New BRS-001",
            $"New process {retryRequest.Id:N} sent (correlation: corr-rej-002)"));
        // await Task.Delay(2500, ct);

        // ── Step 7: DataHub Acknowledges Retry ──
        await setup.StateMachine.MarkAcknowledgedAsync(retryRequest.Id, ct);

        await onStepCompleted(new SimulationStep(7, "DataHub Acknowledges Retry",
            "Retry acknowledged, moved to effectuation_pending"));
        // await Task.Delay(2000, ct);

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
        // await Task.Delay(1500, ct);

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
        // await Task.Delay(1200, ct);

        // ── Step 6: Receive January Metering ──
        // In production, RSM-012 arrives once per day (~02:00), covering the previous 24 hours.
        // Here we simulate 31 daily deliveries as a single batch.
        var janRows = GenerateMeteringData(start, 744, 0.55m, "msg-aconto-jan");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, janRows, ct);

        await onStepCompleted(new SimulationStep(6, "Receive January Metering",
            $"31 daily RSM-012 deliveries — 744 hourly readings ({744 * 0.55m:N1} kWh total)"));
        // await Task.Delay(1000, ct);

        // ── Step 7: Receive February Metering ──
        var febStart = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var febRows = GenerateMeteringData(febStart, 672, 0.55m, "msg-aconto-feb");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, febRows, ct);

        var febPrices = GenerateSpotPrices("DK1", febStart, 672);
        await setup.SpotPriceRepo.StorePricesAsync(febPrices, ct);

        await onStepCompleted(new SimulationStep(7, "Receive February Metering",
            $"28 daily RSM-012 deliveries — 672 hourly readings ({672 * 0.55m:N1} kWh total)"));
        // await Task.Delay(1000, ct);

        // ── Step 8: Receive March Metering ──
        var marStart = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var marRows = GenerateMeteringData(marStart, 744, 0.55m, "msg-aconto-mar");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, marRows, ct);

        var marPrices = GenerateSpotPrices("DK1", marStart, 744);
        await setup.SpotPriceRepo.StorePricesAsync(marPrices, ct);

        await onStepCompleted(new SimulationStep(8, "Receive March Metering",
            $"31 daily RSM-012 deliveries — 744 hourly readings ({744 * 0.55m:N1} kWh total)"));
        // await Task.Delay(1000, ct);

        // ── Step 9: Run Q1 Settlement ──
        var q1End = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var q1Consumption = await setup.MeteringRepo.GetConsumptionAsync(Gsrn, start, q1End, ct);
        var q1SpotPrices = await setup.SpotPriceRepo.GetPricesAsync("DK1", start, q1End, ct);
        var rates = await setup.TariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await setup.TariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct) ?? 0m;
        var gridSub = await setup.TariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct) ?? 0m;

        // Calculate each month separately and sum (subscriptions are per-month)
        var engine = new SettlementEngine();
        var janResult = engine.Calculate(new SettlementRequest(Gsrn,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1),
            q1Consumption.Where(r => r.Timestamp < febStart).ToList(),
            q1SpotPrices.Where(p => p.Timestamp < febStart).ToList(),
            rates, 0.054m, 0.049m, elTax, gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth));
        var febResult = engine.Calculate(new SettlementRequest(Gsrn,
            new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 1),
            q1Consumption.Where(r => r.Timestamp >= febStart && r.Timestamp < marStart).ToList(),
            q1SpotPrices.Where(p => p.Timestamp >= febStart && p.Timestamp < marStart).ToList(),
            rates, 0.054m, 0.049m, elTax, gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth));
        var marResult = engine.Calculate(new SettlementRequest(Gsrn,
            new DateOnly(2025, 3, 1), new DateOnly(2025, 4, 1),
            q1Consumption.Where(r => r.Timestamp >= marStart).ToList(),
            q1SpotPrices.Where(p => p.Timestamp >= marStart).ToList(),
            rates, 0.054m, 0.049m, elTax, gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth));

        var q1Total = janResult.Total + febResult.Total + marResult.Total;

        await onStepCompleted(new SimulationStep(9, "Run Q1 Settlement",
            $"Q1 actual: {q1Total:N2} DKK (Jan {janResult.Total:N2} + Feb {febResult.Total:N2} + Mar {marResult.Total:N2})"));
        // await Task.Delay(1500, ct);

        // ── Step 10: Aconto Reconciliation ──
        var difference = q1Total - quarterlyEstimate;

        await onStepCompleted(new SimulationStep(10, "Aconto Reconciliation",
            $"Aconto paid {quarterlyEstimate:N2} DKK, actual {q1Total:N2} DKK, difference {difference:N2} DKK"));
        // await Task.Delay(1200, ct);

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
        // await Task.Delay(2500, ct);

        // ── Step 5: DataHub Acknowledges ──
        await setup.StateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(5, "DataHub Acknowledges",
            "Process acknowledged, moved to effectuation_pending"));
        // await Task.Delay(2000, ct);

        // ── Step 6: Cancel Before Effectuation ──
        await setup.StateMachine.MarkCancelledAsync(processRequest.Id, "Customer changed their mind", ct);

        await onStepCompleted(new SimulationStep(6, "Cancel Before Effectuation",
            "BRS-003 sent, process cancelled before effectuation"));
        // await Task.Delay(2000, ct);

        // ── Step 7: Verify Clean State ──
        var supplyPeriods = await setup.Portfolio.GetSupplyPeriodsAsync(Gsrn, ct);
        var process = await setup.ProcessRepo.GetAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(7, "Verify Clean State",
            $"No supply activated ({supplyPeriods.Count} periods), process status: {process?.Status ?? "unknown"}"));
    }

    // ── Scenario 5: Move In (Sunshine) ─────────────────────────────

    public async Task RunMoveInSunshineAsync(Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var setup = await SeedCommonDataAsync(onStepCompleted, ct, createSupplyPeriod: false);
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // ── Step 4: Submit BRS-009 ──
        var processRequest = await setup.StateMachine.CreateRequestAsync(Gsrn, "move_in", new DateOnly(2025, 1, 1), ct);
        await setup.StateMachine.MarkSentAsync(processRequest.Id, "corr-movein-001", ct);

        await onStepCompleted(new SimulationStep(4, "Submit BRS-009",
            $"Process {processRequest.Id:N} created and sent to DataHub"));
        // await Task.Delay(2500, ct);

        // ── Step 5: DataHub Acknowledges ──
        await setup.StateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(5, "DataHub Acknowledges",
            "Process acknowledged and moved to effectuation_pending"));
        // await Task.Delay(2000, ct);

        // ── Step 6: Receive RSM-022 ──
        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES ('msg-rsm022-movein', 'RSM-022', 'corr-movein-001', 'MasterData', 'processed', 1024)
                """);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES ('msg-rsm022-movein')
                """);
        }
        await setup.Portfolio.CreateSupplyPeriodAsync(Gsrn, new DateOnly(2025, 1, 1), ct);
        await setup.Portfolio.ActivateMeteringPointAsync(Gsrn, start, ct);

        await onStepCompleted(new SimulationStep(6, "Receive RSM-022",
            "Inbound RSM-022 recorded, supply period created, metering point activated"));
        // await Task.Delay(1000, ct);

        // ── Step 7: Complete Process ──
        await setup.StateMachine.MarkCompletedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(7, "Complete Process",
            "Move-in process completed"));
        // await Task.Delay(3000, ct);

        // ── Step 8: Receive RSM-012 ──
        var rows = GenerateMeteringData(start, 744, 0.55m, "msg-rsm012-movein");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, rows, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES ('msg-rsm012-movein', 'RSM-012', NULL, 'Timeseries', 'processed', 52000)
                """);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES ('msg-rsm012-movein')
                """);
        }

        await onStepCompleted(new SimulationStep(8, "Receive RSM-012",
            "31 daily RSM-012 deliveries — 744 hourly readings (409.200 kWh)"));
        // await Task.Delay(1500, ct);

        // ── Step 9: Run Settlement ──
        var result = await RunSettlementAsync(setup, start, start.AddMonths(1), ct);

        await onStepCompleted(new SimulationStep(9, "Run Settlement",
            $"Settlement complete — subtotal {result.Subtotal:N2} DKK, VAT {result.VatAmount:N2} DKK, total {result.Total:N2} DKK"));
    }

    // ── Scenario 6: Move Out ─────────────────────────────────────────

    public async Task RunMoveOutScenarioAsync(Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var setup = await SeedCommonDataAsync(onStepCompleted, ct);
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // ── Step 4: Establish Supply (abbreviated BRS-001) ──
        var onboardProcess = await setup.StateMachine.CreateRequestAsync(Gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await setup.StateMachine.MarkSentAsync(onboardProcess.Id, "corr-moveout-setup", ct);
        await setup.StateMachine.MarkAcknowledgedAsync(onboardProcess.Id, ct);
        await setup.StateMachine.MarkCompletedAsync(onboardProcess.Id, ct);
        await setup.Portfolio.ActivateMeteringPointAsync(Gsrn, start, ct);

        await onStepCompleted(new SimulationStep(4, "Establish Supply",
            "BRS-001 sent, acknowledged, completed — supply active from 2025-01-01"));
        // await Task.Delay(1500, ct);

        // ── Step 5: Receive January RSM-012 ──
        var janRows = GenerateMeteringData(start, 744, 0.55m, "msg-rsm012-moveout-jan");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, janRows, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES ('msg-rsm012-moveout-jan', 'RSM-012', NULL, 'Timeseries', 'processed', 52000)
                """);
        }

        await onStepCompleted(new SimulationStep(5, "Receive January RSM-012",
            "744 hourly readings (409.200 kWh)"));
        // await Task.Delay(1000, ct);

        // ── Step 6: Run January Settlement ──
        var janResult = await RunSettlementAsync(setup, start, start.AddMonths(1), ct);

        await onStepCompleted(new SimulationStep(6, "Run January Settlement",
            $"Total: {janResult.Total:N2} DKK"));
        // await Task.Delay(1500, ct);

        // ── Step 7: Submit BRS-010 (Move Out) ──
        var moveOutProcess = await setup.StateMachine.CreateRequestAsync(Gsrn, "move_out", new DateOnly(2025, 2, 16), ct);
        await setup.StateMachine.MarkSentAsync(moveOutProcess.Id, "corr-moveout-001", ct);

        await onStepCompleted(new SimulationStep(7, "Submit BRS-010",
            $"Move-out process {moveOutProcess.Id:N} sent to DataHub"));
        // await Task.Delay(2500, ct);

        // ── Step 8: DataHub Acknowledges + Complete ──
        await setup.StateMachine.MarkAcknowledgedAsync(moveOutProcess.Id, ct);
        await setup.StateMachine.MarkCompletedAsync(moveOutProcess.Id, ct);

        await onStepCompleted(new SimulationStep(8, "DataHub Acknowledges",
            "Move-out acknowledged and completed"));
        // await Task.Delay(2000, ct);

        // ── Step 9: Receive Final RSM-012 ──
        var finalStart = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var finalRows = GenerateMeteringData(finalStart, 360, 0.55m, "msg-rsm012-moveout-final");
        await setup.MeteringRepo.StoreTimeSeriesAsync(Gsrn, finalRows, ct);

        var febPrices = GenerateSpotPrices("DK1", finalStart, 360);
        await setup.SpotPriceRepo.StorePricesAsync(febPrices, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES ('msg-rsm012-moveout-final', 'RSM-012', NULL, 'Timeseries', 'processed', 25000)
                """);
        }

        await onStepCompleted(new SimulationStep(9, "Receive Final RSM-012",
            "360 hourly readings (Feb 1-16), final metering data before move-out"));
        // await Task.Delay(1500, ct);

        // ── Step 10: Final Settlement + Deactivate ──
        var finalEnd = finalStart.AddHours(360);
        var finalConsumption = await setup.MeteringRepo.GetConsumptionAsync(Gsrn, finalStart, finalEnd, ct);
        var finalSpotPrices = await setup.SpotPriceRepo.GetPricesAsync("DK1", finalStart, finalEnd, ct);
        var rates = await setup.TariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await setup.TariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct) ?? 0m;
        var gridSub = await setup.TariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct) ?? 0m;

        var engine = new SettlementEngine();
        var finalService = new FinalSettlementService(engine);
        var finalRequest = new SettlementRequest(
            Gsrn,
            new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 16),
            finalConsumption, finalSpotPrices,
            rates, 0.054m, 0.049m, elTax,
            gridSub,
            setup.Product.MarginOrePerKwh / 100m,
            (setup.Product.SupplementOrePerKwh ?? 0m) / 100m,
            setup.Product.SubscriptionKrPerMonth);
        var finalResult = finalService.CalculateFinal(finalRequest, acontoPaid: null);

        await setup.Portfolio.EndSupplyPeriodAsync(Gsrn, new DateOnly(2025, 2, 16), "move_out", ct);
        await setup.Portfolio.EndContractAsync(Gsrn, new DateOnly(2025, 2, 16), ct);
        await setup.Portfolio.DeactivateMeteringPointAsync(Gsrn, finalEnd, ct);
        await setup.StateMachine.MarkOffboardingAsync(moveOutProcess.Id, ct);
        await setup.StateMachine.MarkFinalSettledAsync(moveOutProcess.Id, ct);

        await onStepCompleted(new SimulationStep(10, "Final Settlement + Deactivate",
            $"Final settlement — total {finalResult.TotalDue:N2} DKK, customer moved out"));
    }

    // ── Operations: Change of Supplier (concurrent-safe) ───────────

    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public async Task RunChangeOfSupplierAsync(
        string gsrn, string customerName,
        Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var check = await MarketRules.CanChangeSupplierAsync(gsrn, _connectionString, ct);
        if (!check.IsValid)
            throw new InvalidOperationException(check.ErrorMessage);

        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var processRepo = new ProcessRepository(_connectionString);
        var stateMachine = new ProcessStateMachine(processRepo, _clock);

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
        // await Task.Delay(800, ct);

        // ── Step 2: Create Customer & Metering Point ──
        var customer = await portfolio.CreateCustomerAsync(customerName, "0101901234", "private", null, ct);
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
        // await Task.Delay(800, ct);

        // ── Step 3: Submit BRS-001 ──
        var uid = Guid.NewGuid().ToString("N")[..8];
        var corrId = $"corr-ops-{uid}";
        var processRequest = await stateMachine.CreateRequestAsync(gsrn, "supplier_switch", new DateOnly(2025, 1, 1), ct);
        await stateMachine.MarkSentAsync(processRequest.Id, corrId, ct);

        await onStepCompleted(new SimulationStep(3, "Submit BRS-001",
            $"Process {processRequest.Id:N} sent"));
        // await Task.Delay(1500, ct);

        // ── Step 4: DataHub Acknowledges ──
        await stateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(4, "DataHub Acknowledges",
            "Process acknowledged → effectuation_pending"));
        // await Task.Delay(1200, ct);

        // ── Step 5: Receive RSM-022 ──
        var msgId022 = $"msg-rsm022-ops-{uid}";
        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES (@MsgId, 'RSM-022', @CorrId, 'MasterData', 'processed', 1024)
                """, new { MsgId = msgId022, CorrId = corrId });
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                """, new { MsgId = msgId022 });
        }
        await portfolio.ActivateMeteringPointAsync(gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);

        await onStepCompleted(new SimulationStep(5, "Receive RSM-022",
            "Metering point activated"));
        // await Task.Delay(1000, ct);

        // ── Step 6: Complete Process ──
        await stateMachine.MarkCompletedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(6, "Complete Process",
            "Supplier switch completed"));
        // await Task.Delay(1500, ct);

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
        // await Task.Delay(1000, ct);

        // ── Step 8: Run Settlement ──
        var periodEnd = start.AddMonths(1);
        var consumption = await meteringRepo.GetConsumptionAsync(gsrn, start, periodEnd, ct);
        var spotPrices = await spotPriceRepo.GetPricesAsync("DK1", start, periodEnd, ct);
        var rates = await tariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await tariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct) ?? 0m;
        var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct) ?? 0m;

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
            await conn.ExecuteAsync("SELECT pg_advisory_lock(hashtext(@Key))", new { Key = gsrn });
            try
            {
                var billingPeriodId = await conn.QuerySingleAsync<Guid>("""
                    INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                    VALUES (@PeriodStart, @PeriodEnd, 'monthly')
                    ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
                    RETURNING id
                    """, new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd });

                var settlementRunId = await conn.QuerySingleAsync<Guid>("""
                    INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, metering_points_count)
                    VALUES (
                        @BillingPeriodId, '344', @MeteringPointId,
                        COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                  WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                        'completed', 1)
                    RETURNING id
                    """, new { BillingPeriodId = billingPeriodId, MeteringPointId = gsrn });

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
                await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))", new { Key = gsrn });
            }
        }

        await onStepCompleted(new SimulationStep(8, "Run Settlement",
            $"Total: {result.Total:N2} DKK (subtotal {result.Subtotal:N2}, VAT {result.VatAmount:N2})"));
    }

    // ── Operations: Receive Metering ────────────────────────────────

    public async Task ReceiveMeteringOpsAsync(
        string gsrn, Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var check = await MarketRules.CanReceiveMeteringAsync(gsrn, _connectionString, ct);
        if (!check.IsValid)
            throw new InvalidOperationException(check.ErrorMessage);

        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);

        // ── Step 1: Determine next month + seed spot prices ──
        DateTime latestTimestamp;
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            latestTimestamp = await conn.QuerySingleAsync<DateTime>(
                "SELECT MAX(timestamp) FROM metering.metering_data WHERE metering_point_id = @Gsrn",
                new { Gsrn = gsrn });
        }

        var nextMonthStart = new DateTime(latestTimestamp.Year, latestTimestamp.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(1);
        var hoursInMonth = (int)(nextMonthStart.AddMonths(1) - nextMonthStart).TotalHours;

        var prices = GenerateSpotPrices("DK1", nextMonthStart, hoursInMonth);
        await spotPriceRepo.StorePricesAsync(prices, ct);

        var monthName = nextMonthStart.ToString("MMMM yyyy");
        await onStepCompleted(new SimulationStep(1, "Seed Spot Prices",
            $"Seeded {hoursInMonth} spot prices for {monthName}"));
        // await Task.Delay(1000, ct);

        // ── Step 2: Generate + store hourly readings ──
        var uid = Guid.NewGuid().ToString("N")[..8];
        var msgId = $"msg-rsm012-ops-{uid}";
        var rows = GenerateMeteringData(nextMonthStart, hoursInMonth, 0.55m, msgId);
        await meteringRepo.StoreTimeSeriesAsync(gsrn, rows, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES (@MsgId, 'RSM-012', NULL, 'Timeseries', 'processed', 52000)
                """, new { MsgId = msgId });
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                """, new { MsgId = msgId });
        }

        var totalKwh = hoursInMonth * 0.55m;
        await onStepCompleted(new SimulationStep(2, "Receive RSM-012",
            $"{hoursInMonth} hourly readings for {monthName} ({totalKwh:N1} kWh)"));
    }

    // ── Operations: Run Settlement ───────────────────────────────────

    public async Task RunSettlementOpsAsync(
        string gsrn, Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var check = await MarketRules.CanRunSettlementAsync(gsrn, _connectionString, ct);
        if (!check.IsValid)
            throw new InvalidOperationException(check.ErrorMessage);

        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);

        // ── Step 1: Load unsettled data ──
        DateTime meteringStart, meteringEnd;
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            // Find the full metering range for this GSRN
            var range = await conn.QuerySingleAsync<(DateTime min, DateTime max)>(
                "SELECT MIN(timestamp), MAX(timestamp) + interval '1 hour' FROM metering.metering_data WHERE metering_point_id = @Gsrn",
                new { Gsrn = gsrn });
            meteringStart = range.min;
            meteringEnd = range.max;

            // Find already-settled range (cast DATE→TIMESTAMPTZ so Npgsql returns DateTime with Kind=Utc)
            var settledEnd = await conn.QuerySingleOrDefaultAsync<DateTime?>("""
                SELECT MAX(bp.period_end::timestamptz)
                FROM settlement.settlement_line sl
                JOIN settlement.settlement_run sr ON sr.id = sl.settlement_run_id
                JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
                WHERE sl.metering_point_id = @Gsrn
                """, new { Gsrn = gsrn });

            if (settledEnd.HasValue)
                meteringStart = settledEnd.Value;
        }

        var periodStart = DateOnly.FromDateTime(meteringStart);
        var periodEnd = DateOnly.FromDateTime(meteringEnd);

        var contract = await portfolio.GetActiveContractAsync(gsrn, ct)
            ?? throw new InvalidOperationException($"No active contract for GSRN {gsrn}");
        var product = await portfolio.GetProductAsync(contract.ProductId, ct)
            ?? throw new InvalidOperationException($"Product {contract.ProductId} not found");

        var consumption = await meteringRepo.GetConsumptionAsync(gsrn, meteringStart, meteringEnd, ct);
        var spotPrices = await spotPriceRepo.GetPricesAsync("DK1", meteringStart, meteringEnd, ct);
        var rates = await tariffRepo.GetRatesAsync("344", "grid", periodStart, ct);
        var elTax = await tariffRepo.GetElectricityTaxAsync(periodStart, ct) ?? 0m;
        var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", periodStart, ct) ?? 0m;

        await onStepCompleted(new SimulationStep(1, "Load Data",
            $"Period {periodStart} to {periodEnd}: {consumption.Count} readings, {spotPrices.Count} prices"));
        // await Task.Delay(1000, ct);

        // ── Step 2: Calculate settlement ──
        var engine = new SettlementEngine();
        var result = engine.Calculate(new SettlementRequest(
            gsrn, periodStart, periodEnd,
            consumption, spotPrices,
            rates, 0.054m, 0.049m, elTax,
            gridSub,
            product.MarginOrePerKwh / 100m,
            (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth));

        await onStepCompleted(new SimulationStep(2, "Calculate Settlement",
            $"Subtotal {result.Subtotal:N2}, VAT {result.VatAmount:N2}, total {result.Total:N2} DKK"));
        // await Task.Delay(1000, ct);

        // ── Step 3: Store settlement ──
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync("SELECT pg_advisory_lock(hashtext(@Key))", new { Key = gsrn });
            try
            {
                var billingPeriodId = await conn.QuerySingleAsync<Guid>("""
                    INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                    VALUES (@PeriodStart, @PeriodEnd, 'monthly')
                    ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
                    RETURNING id
                    """, new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd });

                var settlementRunId = await conn.QuerySingleAsync<Guid>("""
                    INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, metering_points_count)
                    VALUES (
                        @BillingPeriodId, '344', @MeteringPointId,
                        COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                  WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                        'completed', 1)
                    RETURNING id
                    """, new { BillingPeriodId = billingPeriodId, MeteringPointId = gsrn });

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
                await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))", new { Key = gsrn });
            }
        }

        await onStepCompleted(new SimulationStep(3, "Store Settlement",
            $"Total: {result.Total:N2} DKK ({result.TotalKwh:N1} kWh, {periodStart}–{periodEnd})"));
    }

    // ── Operations: Offboard ─────────────────────────────────────────

    public async Task OffboardOpsAsync(
        string gsrn, Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var check = await MarketRules.CanOffboardAsync(gsrn, _connectionString, ct);
        if (!check.IsValid)
            throw new InvalidOperationException(check.ErrorMessage);

        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var processRepo = new ProcessRepository(_connectionString);
        var stateMachine = new ProcessStateMachine(processRepo, _clock);

        // ── Step 1: Mark process as offboarding ──
        Guid processId;
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            processId = await conn.QuerySingleAsync<Guid>(
                "SELECT id FROM lifecycle.process_request WHERE gsrn = @Gsrn AND status = 'completed' ORDER BY created_at DESC LIMIT 1",
                new { Gsrn = gsrn });
        }
        await stateMachine.MarkOffboardingAsync(processId, ct);

        await onStepCompleted(new SimulationStep(1, "Start Offboarding",
            $"Process {processId:N} marked as offboarding"));
        // await Task.Delay(1200, ct);

        // ── Step 2: Seed spot prices + receive final metering ──
        DateTime? latestTimestamp;
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            latestTimestamp = await conn.QuerySingleOrDefaultAsync<DateTime?>(
                "SELECT MAX(timestamp) FROM metering.metering_data WHERE metering_point_id = @Gsrn",
                new { Gsrn = gsrn });
        }

        // If no metering data yet (e.g. aconto customer), use effective date from the process
        DateTime departureStart;
        if (latestTimestamp.HasValue)
        {
            departureStart = new DateTime(latestTimestamp.Value.Year, latestTimestamp.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(1);
        }
        else
        {
            var latestProcess = (await processRepo.GetByStatusAsync("completed", ct)).FirstOrDefault(p => p.Gsrn == gsrn);
            var effDate = latestProcess?.EffectiveDate ?? _clock.Today;
            departureStart = effDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }
        const int finalHours = 360; // ~15 days partial month

        var prices = GenerateSpotPrices("DK1", departureStart, finalHours);
        await spotPriceRepo.StorePricesAsync(prices, ct);

        var uid = Guid.NewGuid().ToString("N")[..8];
        var msgId = $"msg-rsm012-final-{uid}";
        var rows = GenerateMeteringData(departureStart, finalHours, 0.55m, msgId);
        await meteringRepo.StoreTimeSeriesAsync(gsrn, rows, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES (@MsgId, 'RSM-012', NULL, 'Timeseries', 'processed', 25000)
                """, new { MsgId = msgId });
        }

        var departureEnd = departureStart.AddHours(finalHours);
        await onStepCompleted(new SimulationStep(2, "Final Metering Data",
            $"{finalHours} hourly readings ({finalHours * 0.55m:N1} kWh), departure period"));
        // await Task.Delay(1000, ct);

        // ── Step 3: Run final settlement ──
        var contract = await portfolio.GetActiveContractAsync(gsrn, ct)
            ?? throw new InvalidOperationException($"No active contract for GSRN {gsrn}");
        var product = await portfolio.GetProductAsync(contract.ProductId, ct)
            ?? throw new InvalidOperationException($"Product {contract.ProductId} not found");

        var finalConsumption = await meteringRepo.GetConsumptionAsync(gsrn, departureStart, departureEnd, ct);
        var finalSpotPrices = await spotPriceRepo.GetPricesAsync("DK1", departureStart, departureEnd, ct);
        var rates = await tariffRepo.GetRatesAsync("344", "grid", DateOnly.FromDateTime(departureStart), ct);
        var elTax = await tariffRepo.GetElectricityTaxAsync(DateOnly.FromDateTime(departureStart), ct) ?? 0m;
        var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", DateOnly.FromDateTime(departureStart), ct) ?? 0m;

        var engine = new SettlementEngine();
        var finalService = new FinalSettlementService(engine);
        var finalRequest = new SettlementRequest(
            gsrn,
            DateOnly.FromDateTime(departureStart), DateOnly.FromDateTime(departureEnd),
            finalConsumption, finalSpotPrices,
            rates, 0.054m, 0.049m, elTax,
            gridSub,
            product.MarginOrePerKwh / 100m,
            (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth);
        var finalResult = finalService.CalculateFinal(finalRequest, acontoPaid: null);

        await onStepCompleted(new SimulationStep(3, "Final Settlement",
            $"Total due: {finalResult.TotalDue:N2} DKK"));
        // await Task.Delay(1000, ct);

        // ── Step 4: End supply + contract + deactivate ──
        var departureDate = DateOnly.FromDateTime(departureEnd);
        await portfolio.EndSupplyPeriodAsync(gsrn, departureDate, "supplier_switch", ct);
        await portfolio.EndContractAsync(gsrn, departureDate, ct);
        await portfolio.DeactivateMeteringPointAsync(gsrn, departureEnd, ct);

        await onStepCompleted(new SimulationStep(4, "Deactivate",
            $"Supply ended, contract closed, metering point deactivated"));
        // await Task.Delay(800, ct);

        // ── Step 5: Mark process as final_settled ──
        await stateMachine.MarkFinalSettledAsync(processId, ct);

        await onStepCompleted(new SimulationStep(5, "Final Settled",
            "Process marked as final_settled — offboarding complete"));
    }

    // ── Operations: Aconto Billing ───────────────────────────────────

    public async Task AcontoBillingOpsAsync(
        string gsrn, Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var check = await MarketRules.CanBillAcontoAsync(gsrn, _connectionString, ct);
        if (!check.IsValid)
            throw new InvalidOperationException(check.ErrorMessage);

        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var acontoRepo = new AcontoPaymentRepository(_connectionString);

        var contract = await portfolio.GetActiveContractAsync(gsrn, ct)
            ?? throw new InvalidOperationException($"No active contract for GSRN {gsrn}");
        var product = await portfolio.GetProductAsync(contract.ProductId, ct)
            ?? throw new InvalidOperationException($"Product {contract.ProductId} not found");

        // ── Step 1: Estimate quarterly aconto ──
        var expectedPrice = AcontoEstimator.CalculateExpectedPricePerKwh(
            averageSpotPriceOrePerKwh: 75m, marginOrePerKwh: product.MarginOrePerKwh,
            systemTariffRate: 0.054m, transmissionTariffRate: 0.049m,
            electricityTaxRate: 0.008m, averageGridTariffRate: 0.18m);
        var gridSubRate = 49.00m;
        var supplierSubRate = product.SubscriptionKrPerMonth;
        var quarterlyEstimate = AcontoEstimator.EstimateQuarterlyAmount(
            annualConsumptionKwh: 4000m, expectedPrice, gridSubRate, supplierSubRate);

        await onStepCompleted(new SimulationStep(1, "Estimate Aconto",
            $"Quarterly estimate: {quarterlyEstimate:N2} DKK (4,000 kWh/year)"));
        // await Task.Delay(1000, ct);

        // ── Step 2: Record aconto payment ──
        // Determine quarter start from latest metering data
        DateTime latestTimestamp;
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            latestTimestamp = await conn.QuerySingleAsync<DateTime>(
                "SELECT MAX(timestamp) FROM metering.metering_data WHERE metering_point_id = @Gsrn",
                new { Gsrn = gsrn });
        }

        var quarterStart = new DateTime(latestTimestamp.Year, latestTimestamp.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(1);
        var qStartDate = DateOnly.FromDateTime(quarterStart);
        var qEndDate = qStartDate.AddMonths(3).AddDays(-1);

        await acontoRepo.RecordPaymentAsync(gsrn, qStartDate, qEndDate, quarterlyEstimate, ct);

        await onStepCompleted(new SimulationStep(2, "Record Payment",
            $"Aconto payment of {quarterlyEstimate:N2} DKK recorded for {qStartDate}–{qEndDate}"));
        // await Task.Delay(1000, ct);

        // ── Step 3: Receive 2 more months of metering data ──
        var month1Start = quarterStart;
        var month1Hours = (int)(month1Start.AddMonths(1) - month1Start).TotalHours;
        var month2Start = month1Start.AddMonths(1);
        var month2Hours = (int)(month2Start.AddMonths(1) - month2Start).TotalHours;

        var uid = Guid.NewGuid().ToString("N")[..8];

        var month1Prices = GenerateSpotPrices("DK1", month1Start, month1Hours);
        await spotPriceRepo.StorePricesAsync(month1Prices, ct);
        var month1Rows = GenerateMeteringData(month1Start, month1Hours, 0.55m, $"msg-aconto-m1-{uid}");
        await meteringRepo.StoreTimeSeriesAsync(gsrn, month1Rows, ct);

        var month2Prices = GenerateSpotPrices("DK1", month2Start, month2Hours);
        await spotPriceRepo.StorePricesAsync(month2Prices, ct);
        var month2Rows = GenerateMeteringData(month2Start, month2Hours, 0.55m, $"msg-aconto-m2-{uid}");
        await meteringRepo.StoreTimeSeriesAsync(gsrn, month2Rows, ct);

        var totalNewKwh = (month1Hours + month2Hours) * 0.55m;
        await onStepCompleted(new SimulationStep(3, "Receive Metering",
            $"2 months: {month1Hours + month2Hours} readings ({totalNewKwh:N1} kWh)"));
        // await Task.Delay(1000, ct);

        // ── Step 4: Reconcile — actual vs aconto ──
        // We now have 3 months: original month (already in DB) + 2 new months
        // Calculate actual cost for the quarter
        var originalEnd = new DateTime(latestTimestamp.Year, latestTimestamp.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(1);
        // Settle each of the 3 months in the quarter range
        // Month already settled = Jan (from Change of Supplier). New months = the 2 we just added.
        // Reconcile against the 2 new months only (Jan already settled separately)
        var reconcileStart = month1Start;
        var reconcileEnd = month2Start.AddMonths(1);

        var rates = await tariffRepo.GetRatesAsync("344", "grid", qStartDate, ct);
        var elTax = await tariffRepo.GetElectricityTaxAsync(qStartDate, ct) ?? 0m;
        var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", qStartDate, ct) ?? 0m;

        var consumption = await meteringRepo.GetConsumptionAsync(gsrn, reconcileStart, reconcileEnd, ct);
        var spotPrices = await spotPriceRepo.GetPricesAsync("DK1", reconcileStart, reconcileEnd, ct);

        // Calculate per-month and sum
        var engine = new SettlementEngine();
        var m1Result = engine.Calculate(new SettlementRequest(gsrn,
            DateOnly.FromDateTime(month1Start), DateOnly.FromDateTime(month2Start),
            consumption.Where(r => r.Timestamp < month2Start).ToList(),
            spotPrices.Where(p => p.Timestamp < month2Start).ToList(),
            rates, 0.054m, 0.049m, elTax, gridSub,
            product.MarginOrePerKwh / 100m,
            (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth));
        var m2Result = engine.Calculate(new SettlementRequest(gsrn,
            DateOnly.FromDateTime(month2Start), DateOnly.FromDateTime(reconcileEnd),
            consumption.Where(r => r.Timestamp >= month2Start).ToList(),
            spotPrices.Where(p => p.Timestamp >= month2Start).ToList(),
            rates, 0.054m, 0.049m, elTax, gridSub,
            product.MarginOrePerKwh / 100m,
            (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth));

        var actualTotal = m1Result.Total + m2Result.Total;
        var difference = actualTotal - quarterlyEstimate;

        await onStepCompleted(new SimulationStep(4, "Reconcile",
            $"Actual: {actualTotal:N2} DKK, aconto paid: {quarterlyEstimate:N2}, difference: {difference:N2} DKK"));
    }

    // ── Operations: Move In ─────────────────────────────────────────

    public async Task RunMoveInAsync(
        string gsrn, string customerName,
        Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var check = await MarketRules.CanMoveInAsync(gsrn, _connectionString, ct);
        if (!check.IsValid)
            throw new InvalidOperationException(check.ErrorMessage);

        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var processRepo = new ProcessRepository(_connectionString);
        var stateMachine = new ProcessStateMachine(processRepo, _clock);

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
        // await Task.Delay(800, ct);

        // ── Step 2: Create Customer & Metering Point ──
        var customer = await portfolio.CreateCustomerAsync(customerName, "0101901234", "private", null, ct);
        var product = await portfolio.CreateProductAsync($"Spot-{gsrn[^4..]}", "spot", 4.0m, null, 39.00m, ct);

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
        }

        await onStepCompleted(new SimulationStep(2, "Create Customer & Metering Point",
            $"{customerName}, GSRN {gsrn}, contract (no supply yet — awaiting move-in)"));
        // await Task.Delay(800, ct);

        // ── Step 3: Submit BRS-009 ──
        var uid = Guid.NewGuid().ToString("N")[..8];
        var corrId = $"corr-movein-{uid}";
        var processRequest = await stateMachine.CreateRequestAsync(gsrn, "move_in", new DateOnly(2025, 1, 1), ct);
        await stateMachine.MarkSentAsync(processRequest.Id, corrId, ct);

        await onStepCompleted(new SimulationStep(3, "Submit BRS-009",
            $"Process {processRequest.Id:N} sent"));
        // await Task.Delay(1500, ct);

        // ── Step 4: DataHub Acknowledges ──
        await stateMachine.MarkAcknowledgedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(4, "DataHub Acknowledges",
            "Process acknowledged → effectuation_pending"));
        // await Task.Delay(1200, ct);

        // ── Step 5: Receive RSM-022 + Create Supply ──
        var msgId022 = $"msg-rsm022-movein-{uid}";
        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES (@MsgId, 'RSM-022', @CorrId, 'MasterData', 'processed', 1024)
                """, new { MsgId = msgId022, CorrId = corrId });
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                """, new { MsgId = msgId022 });
        }

        await using (var supplyConn = new NpgsqlConnection(_connectionString))
        {
            await supplyConn.OpenAsync(ct);
            await supplyConn.ExecuteAsync("""
                INSERT INTO portfolio.supply_period (gsrn, start_date)
                SELECT @Gsrn, @StartDate
                WHERE NOT EXISTS (SELECT 1 FROM portfolio.supply_period WHERE gsrn = @Gsrn AND start_date = @StartDate)
                """, new { Gsrn = gsrn, StartDate = new DateOnly(2025, 1, 1) });
        }
        await portfolio.ActivateMeteringPointAsync(gsrn, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);

        await onStepCompleted(new SimulationStep(5, "Receive RSM-022",
            "Supply period created, metering point activated"));
        // await Task.Delay(1000, ct);

        // ── Step 6: Complete Process ──
        await stateMachine.MarkCompletedAsync(processRequest.Id, ct);

        await onStepCompleted(new SimulationStep(6, "Complete Process",
            "Move-in completed"));
        // await Task.Delay(1500, ct);

        // ── Step 7: Receive RSM-012 ──
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var msgId012 = $"msg-rsm012-movein-{uid}";
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
        // await Task.Delay(1000, ct);

        // ── Step 8: Run Settlement ──
        var periodEnd = start.AddMonths(1);
        var consumption = await meteringRepo.GetConsumptionAsync(gsrn, start, periodEnd, ct);
        var spotPrices = await spotPriceRepo.GetPricesAsync("DK1", start, periodEnd, ct);
        var rates = await tariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await tariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct) ?? 0m;
        var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct) ?? 0m;

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
            await conn.ExecuteAsync("SELECT pg_advisory_lock(hashtext(@Key))", new { Key = gsrn });
            try
            {
                var billingPeriodId = await conn.QuerySingleAsync<Guid>("""
                    INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                    VALUES (@PeriodStart, @PeriodEnd, 'monthly')
                    ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
                    RETURNING id
                    """, new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd });

                var settlementRunId = await conn.QuerySingleAsync<Guid>("""
                    INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, metering_points_count)
                    VALUES (
                        @BillingPeriodId, '344', @MeteringPointId,
                        COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                  WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                        'completed', 1)
                    RETURNING id
                    """, new { BillingPeriodId = billingPeriodId, MeteringPointId = gsrn });

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
                await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))", new { Key = gsrn });
            }
        }

        await onStepCompleted(new SimulationStep(8, "Run Settlement",
            $"Total: {result.Total:N2} DKK (subtotal {result.Subtotal:N2}, VAT {result.VatAmount:N2})"));
    }

    // ── Operations: Move Out ─────────────────────────────────────────

    public async Task RunMoveOutAsync(
        string gsrn, Func<SimulationStep, Task> onStepCompleted, CancellationToken ct)
    {
        var check = await MarketRules.CanMoveOutAsync(gsrn, _connectionString, ct);
        if (!check.IsValid)
            throw new InvalidOperationException(check.ErrorMessage);

        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);
        var meteringRepo = new MeteringDataRepository(_connectionString);
        var processRepo = new ProcessRepository(_connectionString);
        var stateMachine = new ProcessStateMachine(processRepo, _clock);

        // ── Step 1: Submit BRS-010 (Move Out) ──
        var uid = Guid.NewGuid().ToString("N")[..8];
        var corrId = $"corr-moveout-{uid}";
        var moveOutProcess = await stateMachine.CreateRequestAsync(gsrn, "move_out", new DateOnly(2025, 2, 16), ct);
        await stateMachine.MarkSentAsync(moveOutProcess.Id, corrId, ct);
        await stateMachine.MarkAcknowledgedAsync(moveOutProcess.Id, ct);
        await stateMachine.MarkCompletedAsync(moveOutProcess.Id, ct);

        await onStepCompleted(new SimulationStep(1, "Submit BRS-010",
            $"Move-out process {moveOutProcess.Id:N} sent, acknowledged, completed"));
        // await Task.Delay(1500, ct);

        // ── Step 2: Final Metering ──
        DateTime? latestTimestamp;
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            latestTimestamp = await conn.QuerySingleOrDefaultAsync<DateTime?>(
                "SELECT MAX(timestamp) FROM metering.metering_data WHERE metering_point_id = @Gsrn",
                new { Gsrn = gsrn });
        }

        DateTime departureStart;
        if (latestTimestamp.HasValue)
        {
            departureStart = new DateTime(latestTimestamp.Value.Year, latestTimestamp.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(1);
        }
        else
        {
            var latestProcess = (await processRepo.GetByStatusAsync("completed", ct)).FirstOrDefault(p => p.Gsrn == gsrn);
            var effDate = latestProcess?.EffectiveDate ?? _clock.Today;
            departureStart = effDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }
        const int finalHours = 360;

        var prices = GenerateSpotPrices("DK1", departureStart, finalHours);
        await spotPriceRepo.StorePricesAsync(prices, ct);

        var msgId = $"msg-rsm012-moveout-{uid}";
        var rows = GenerateMeteringData(departureStart, finalHours, 0.55m, msgId);
        await meteringRepo.StoreTimeSeriesAsync(gsrn, rows, ct);

        await using (var msgConn = new NpgsqlConnection(_connectionString))
        {
            await msgConn.OpenAsync(ct);
            await msgConn.ExecuteAsync("""
                INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                VALUES (@MsgId, 'RSM-012', NULL, 'Timeseries', 'processed', 25000)
                """, new { MsgId = msgId });
        }

        var departureEnd = departureStart.AddHours(finalHours);
        await onStepCompleted(new SimulationStep(2, "Final Metering Data",
            $"{finalHours} hourly readings ({finalHours * 0.55m:N1} kWh), departure period"));
        // await Task.Delay(1000, ct);

        // ── Step 3: Run final settlement ──
        var contract = await portfolio.GetActiveContractAsync(gsrn, ct)
            ?? throw new InvalidOperationException($"No active contract for GSRN {gsrn}");
        var product = await portfolio.GetProductAsync(contract.ProductId, ct)
            ?? throw new InvalidOperationException($"Product {contract.ProductId} not found");

        var finalConsumption = await meteringRepo.GetConsumptionAsync(gsrn, departureStart, departureEnd, ct);
        var finalSpotPrices = await spotPriceRepo.GetPricesAsync("DK1", departureStart, departureEnd, ct);
        var rates = await tariffRepo.GetRatesAsync("344", "grid", DateOnly.FromDateTime(departureStart), ct);
        var elTax = await tariffRepo.GetElectricityTaxAsync(DateOnly.FromDateTime(departureStart), ct) ?? 0m;
        var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", DateOnly.FromDateTime(departureStart), ct) ?? 0m;

        var engine = new SettlementEngine();
        var finalService = new FinalSettlementService(engine);
        var finalRequest = new SettlementRequest(
            gsrn,
            DateOnly.FromDateTime(departureStart), DateOnly.FromDateTime(departureEnd),
            finalConsumption, finalSpotPrices,
            rates, 0.054m, 0.049m, elTax,
            gridSub,
            product.MarginOrePerKwh / 100m,
            (product.SupplementOrePerKwh ?? 0m) / 100m,
            product.SubscriptionKrPerMonth);
        var finalResult = finalService.CalculateFinal(finalRequest, acontoPaid: null);

        await onStepCompleted(new SimulationStep(3, "Final Settlement",
            $"Total due: {finalResult.TotalDue:N2} DKK"));
        // await Task.Delay(1000, ct);

        // ── Step 4: End supply + contract + deactivate ──
        var departureDate = DateOnly.FromDateTime(departureEnd);
        await portfolio.EndSupplyPeriodAsync(gsrn, departureDate, "move_out", ct);
        await portfolio.EndContractAsync(gsrn, departureDate, ct);
        await portfolio.DeactivateMeteringPointAsync(gsrn, departureEnd, ct);

        await onStepCompleted(new SimulationStep(4, "Deactivate",
            "Supply ended, contract closed, metering point deactivated"));
        // await Task.Delay(800, ct);

        // ── Step 5: Mark process as offboarding ──
        await stateMachine.MarkOffboardingAsync(moveOutProcess.Id, ct);

        await onStepCompleted(new SimulationStep(5, "Offboarding",
            "Process marked as offboarding"));
        // await Task.Delay(800, ct);

        // ── Step 6: Mark process as final_settled ──
        await stateMachine.MarkFinalSettledAsync(moveOutProcess.Id, ct);

        await onStepCompleted(new SimulationStep(6, "Final Settled",
            "Process marked as final_settled — move-out complete"));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<SettlementResult> RunSettlementAsync(CommonSetup setup, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        var consumption = await setup.MeteringRepo.GetConsumptionAsync(Gsrn, periodStart, periodEnd, ct);
        var spotPrices = await setup.SpotPriceRepo.GetPricesAsync("DK1", periodStart, periodEnd, ct);
        var rates = await setup.TariffRepo.GetRatesAsync("344", "grid", new DateOnly(2025, 1, 15), ct);
        var elTax = await setup.TariffRepo.GetElectricityTaxAsync(new DateOnly(2025, 1, 15), ct) ?? 0m;
        var gridSub = await setup.TariffRepo.GetSubscriptionAsync("344", "grid", new DateOnly(2025, 1, 15), ct) ?? 0m;

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
            INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, metering_points_count)
            VALUES (
                @BillingPeriodId, '344', @MeteringPointId,
                COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                          WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                'completed', 1)
            RETURNING id
            """, new { BillingPeriodId = billingPeriodId, MeteringPointId = Gsrn });

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

    // ── Tick-Based Methods (date-gated execution) ─────────────────

    private async Task TickSeedAsync(
        string gsrn, string customerName, DateOnly effectiveDate, bool createSupplyPeriod, CancellationToken ct)
    {
        var portfolio = new PortfolioRepository(_connectionString);
        var tariffRepo = new TariffRepository(_connectionString);
        var spotPriceRepo = new SpotPriceRepository(_connectionString);

        await portfolio.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", ct);

        var gridRates = Enumerable.Range(1, 24).Select(h => new TariffRateRow(h, h switch
        {
            >= 1 and <= 6 => 0.06m, >= 7 and <= 16 => 0.18m,
            >= 17 and <= 20 => 0.54m, _ => 0.06m,
        })).ToList();
        await tariffRepo.SeedGridTariffAsync("344", "grid", effectiveDate, gridRates, ct);

        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            var subExists = await conn.QuerySingleAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM tariff.subscription WHERE grid_area_code = '344' AND subscription_type = 'grid')");
            if (!subExists)
                await tariffRepo.SeedSubscriptionAsync("344", "grid", 49.00m, effectiveDate, ct);
        }

        await tariffRepo.SeedElectricityTaxAsync(0.008m, effectiveDate, ct);

        var effectiveStart = effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hoursInMonth = (int)(effectiveStart.AddMonths(1) - effectiveStart).TotalHours;
        var prices = GenerateSpotPrices("DK1", effectiveStart, hoursInMonth);
        await spotPriceRepo.StorePricesAsync(prices, ct);

        var customer = await portfolio.CreateCustomerAsync(customerName, "0101901234", "private", null, ct);
        var product = await portfolio.CreateProductAsync($"Spot-{gsrn[^4..]}", "spot", 4.0m, null, 39.00m, ct);

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
                """, new { CustomerId = customer.Id, Gsrn = gsrn, ProductId = product.Id, StartDate = effectiveDate });
            if (createSupplyPeriod)
            {
                await setupConn.ExecuteAsync("""
                    INSERT INTO portfolio.supply_period (gsrn, start_date)
                    SELECT @Gsrn, @StartDate
                    WHERE NOT EXISTS (SELECT 1 FROM portfolio.supply_period WHERE gsrn = @Gsrn AND start_date = @StartDate)
                    """, new { Gsrn = gsrn, StartDate = effectiveDate });
            }
        }
    }

    public async Task<List<SimulationStep>> TickChangeOfSupplierAsync(
        ChangeOfSupplierContext ctx, DateOnly currentDate, CancellationToken ct)
    {
        var executed = new List<SimulationStep>();
        var timeline = ctx.Timeline;
        var ed = ctx.EffectiveDate;
        var effectiveStart = ed.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hoursInMonth = (int)(effectiveStart.AddMonths(1) - effectiveStart).TotalHours;

        // Step 1: Seed Data
        if (!ctx.IsSeeded && currentDate >= timeline.GetDate("Seed Data"))
        {
            await _seedLock.WaitAsync(ct);
            try
            {
                await TickSeedAsync(ctx.Gsrn, ctx.CustomerName, ed, createSupplyPeriod: true, ct);
            }
            finally
            {
                _seedLock.Release();
            }

            ctx.IsSeeded = true;
            executed.Add(new SimulationStep(1, "Seed Data",
                $"Reference data + customer {ctx.CustomerName} + GSRN {ctx.Gsrn}", currentDate));
        }

        // Step 2: Submit BRS-001
        if (ctx.IsSeeded && !ctx.IsBrsSubmitted && currentDate >= timeline.GetDate("Submit BRS-001"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            var uid = Guid.NewGuid().ToString("N")[..8];
            var corrId = $"corr-ops-{uid}";
            var processRequest = await stateMachine.CreateRequestAsync(ctx.Gsrn, "supplier_switch", ed, ct);
            await stateMachine.MarkSentAsync(processRequest.Id, corrId, ct);
            ctx.ProcessRequestId = processRequest.Id;
            ctx.IsBrsSubmitted = true;
            executed.Add(new SimulationStep(2, "Submit BRS-001",
                $"Process {processRequest.Id:N} sent", currentDate));
        }

        // Step 3: DataHub Acknowledges
        if (ctx.IsBrsSubmitted && !ctx.IsAcknowledged && currentDate >= timeline.GetDate("DataHub Acknowledges"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            await stateMachine.MarkAcknowledgedAsync(ctx.ProcessRequestId, ct);
            ctx.IsAcknowledged = true;
            executed.Add(new SimulationStep(3, "DataHub Acknowledges",
                "Process acknowledged", currentDate));
        }

        // Step 4: Receive RSM-022
        if (ctx.IsAcknowledged && !ctx.IsRsm022Received && currentDate >= timeline.GetDate("Receive RSM-022"))
        {
            var portfolio = new PortfolioRepository(_connectionString);
            var uid = Guid.NewGuid().ToString("N")[..8];
            var msgId022 = $"msg-rsm022-ops-{uid}";
            await using (var msgConn = new NpgsqlConnection(_connectionString))
            {
                await msgConn.OpenAsync(ct);
                await msgConn.ExecuteAsync("""
                    INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                    VALUES (@MsgId, 'RSM-022', @CorrId, 'MasterData', 'processed', 1024)
                    """, new { MsgId = msgId022, CorrId = $"corr-ops-{uid}" });
                await msgConn.ExecuteAsync("""
                    INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                    """, new { MsgId = msgId022 });
            }
            await portfolio.ActivateMeteringPointAsync(ctx.Gsrn, effectiveStart, ct);
            ctx.IsRsm022Received = true;
            executed.Add(new SimulationStep(4, "Receive RSM-022",
                "Metering point activated", currentDate));
        }

        // Step 5: Effectuation + Complete
        if (ctx.IsRsm022Received && !ctx.IsEffectuated && currentDate >= timeline.GetDate("Effectuation"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            await stateMachine.MarkCompletedAsync(ctx.ProcessRequestId, ct);
            ctx.IsEffectuated = true;
            executed.Add(new SimulationStep(5, "Effectuation",
                "Supply begins, process completed", currentDate));
        }

        // Step 6: Receive RSM-012 (daily deliveries — each day delivers previous day's 24h)
        if (ctx.IsEffectuated && !ctx.IsMeteringReceived)
        {
            // Data for day N arrives on day N+1. Deliverable days = D+1 .. min(currentDate, periodEnd+2)
            var firstDeliveryDate = ed.AddDays(1);
            var lastPossibleDelivery = ed.AddMonths(1).AddDays(2);
            var deliverUpTo = currentDate < lastPossibleDelivery ? currentDate : lastPossibleDelivery;

            if (deliverUpTo >= firstDeliveryDate && ctx.MeteringDaysDelivered < ctx.TotalMeteringDays)
            {
                var meteringRepo = new MeteringDataRepository(_connectionString);
                var daysToDeliver = deliverUpTo.DayNumber - firstDeliveryDate.DayNumber + 1;
                var targetDays = Math.Min(daysToDeliver, ctx.TotalMeteringDays);
                var newDays = targetDays - ctx.MeteringDaysDelivered;

                if (newDays > 0)
                {
                    // Generate readings for the newly delivered days
                    var dayOffset = ctx.MeteringDaysDelivered;
                    var batchStart = effectiveStart.AddDays(dayOffset);
                    var batchHours = newDays * 24;
                    var uid = Guid.NewGuid().ToString("N")[..8];
                    var msgId012 = $"msg-rsm012-ops-{uid}";
                    var rows = GenerateMeteringData(batchStart, batchHours, 0.55m, msgId012);
                    await meteringRepo.StoreTimeSeriesAsync(ctx.Gsrn, rows, ct);

                    await using (var msgConn = new NpgsqlConnection(_connectionString))
                    {
                        await msgConn.OpenAsync(ct);
                        await msgConn.ExecuteAsync("""
                            INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                            VALUES (@MsgId, 'RSM-012', NULL, 'Timeseries', 'processed', @Size)
                            """, new { MsgId = msgId012, Size = batchHours * 70 });
                        await msgConn.ExecuteAsync("""
                            INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                            """, new { MsgId = msgId012 });
                    }

                    ctx.MeteringDaysDelivered = targetDays;
                    var totalKwh = targetDays * 24 * 0.55m;
                    var label = ctx.MeteringDaysDelivered >= ctx.TotalMeteringDays
                        ? $"All {ctx.TotalMeteringDays} days received ({totalKwh:N1} kWh)"
                        : $"Day {ctx.MeteringDaysDelivered}/{ctx.TotalMeteringDays} ({totalKwh:N1} kWh)";
                    executed.Add(new SimulationStep(6, "Receive RSM-012", label, currentDate));

                    if (ctx.MeteringDaysDelivered >= ctx.TotalMeteringDays)
                        ctx.IsMeteringReceived = true;
                }
            }
        }

        // Step 7: Run Settlement
        if (ctx.IsMeteringReceived && !ctx.IsSettled && currentDate >= timeline.GetDate("Run Settlement"))
        {
            var portfolio = new PortfolioRepository(_connectionString);
            var tariffRepo = new TariffRepository(_connectionString);
            var spotPriceRepo = new SpotPriceRepository(_connectionString);
            var meteringRepo = new MeteringDataRepository(_connectionString);

            var periodEnd = effectiveStart.AddMonths(1);

            var contract = await portfolio.GetActiveContractAsync(ctx.Gsrn, ct)
                ?? throw new InvalidOperationException($"No active contract for GSRN {ctx.Gsrn}");
            var product = await portfolio.GetProductAsync(contract.ProductId, ct)
                ?? throw new InvalidOperationException($"Product {contract.ProductId} not found");

            var consumption = await meteringRepo.GetConsumptionAsync(ctx.Gsrn, effectiveStart, periodEnd, ct);
            var spotPrices = await spotPriceRepo.GetPricesAsync("DK1", effectiveStart, periodEnd, ct);
            var midMonth = ed.AddDays(14);
            var rates = await tariffRepo.GetRatesAsync("344", "grid", midMonth, ct);
            var elTax = await tariffRepo.GetElectricityTaxAsync(midMonth, ct) ?? 0m;
            var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", midMonth, ct) ?? 0m;

            var engine = new SettlementEngine();
            var result = engine.Calculate(new SettlementRequest(
                ctx.Gsrn,
                ed, DateOnly.FromDateTime(periodEnd),
                consumption, spotPrices,
                rates, 0.054m, 0.049m, elTax, gridSub,
                product.MarginOrePerKwh / 100m,
                (product.SupplementOrePerKwh ?? 0m) / 100m,
                product.SubscriptionKrPerMonth));

            await using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                await conn.ExecuteAsync("SELECT pg_advisory_lock(hashtext(@Key))", new { Key = ctx.Gsrn });
                try
                {
                    var billingPeriodId = await conn.QuerySingleAsync<Guid>("""
                        INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                        VALUES (@PeriodStart, @PeriodEnd, 'monthly')
                        ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
                        RETURNING id
                        """, new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd });

                    var settlementRunId = await conn.QuerySingleAsync<Guid>("""
                        INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, metering_points_count)
                        VALUES (
                            @BillingPeriodId, '344', @MeteringPointId,
                            COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                      WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                            'completed', 1)
                        RETURNING id
                        """, new { BillingPeriodId = billingPeriodId, MeteringPointId = ctx.Gsrn });

                    foreach (var line in result.Lines)
                    {
                        await conn.ExecuteAsync("""
                            INSERT INTO settlement.settlement_line (settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency)
                            VALUES (@RunId, @Gsrn, @ChargeType, @TotalKwh, @TotalAmount, @VatAmount, 'DKK')
                            """, new
                        {
                            RunId = settlementRunId,
                            Gsrn = ctx.Gsrn,
                            line.ChargeType,
                            TotalKwh = line.Kwh ?? 0m,
                            TotalAmount = line.Amount,
                            VatAmount = Math.Round(line.Amount * 0.25m, 2),
                        });
                    }
                }
                finally
                {
                    await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))", new { Key = ctx.Gsrn });
                }
            }

            ctx.IsSettled = true;
            executed.Add(new SimulationStep(7, "Run Settlement",
                $"Total: {result.Total:N2} DKK (subtotal {result.Subtotal:N2}, VAT {result.VatAmount:N2})", currentDate));
        }

        return executed;
    }

    public async Task<List<SimulationStep>> TickMoveInAsync(
        MoveInContext ctx, DateOnly currentDate, CancellationToken ct)
    {
        var executed = new List<SimulationStep>();
        var timeline = ctx.Timeline;
        var ed = ctx.EffectiveDate;
        var effectiveStart = ed.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hoursInMonth = (int)(effectiveStart.AddMonths(1) - effectiveStart).TotalHours;

        // Step 1: Seed Data
        if (!ctx.IsSeeded && currentDate >= timeline.GetDate("Seed Data"))
        {
            await _seedLock.WaitAsync(ct);
            try
            {
                await TickSeedAsync(ctx.Gsrn, ctx.CustomerName, ed, createSupplyPeriod: false, ct);
            }
            finally
            {
                _seedLock.Release();
            }

            ctx.IsSeeded = true;
            executed.Add(new SimulationStep(1, "Seed Data",
                $"Reference data + customer {ctx.CustomerName} + GSRN {ctx.Gsrn}", currentDate));
        }

        // Step 2: Submit BRS-009
        if (ctx.IsSeeded && !ctx.IsBrsSubmitted && currentDate >= timeline.GetDate("Submit BRS-009"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            var uid = Guid.NewGuid().ToString("N")[..8];
            var corrId = $"corr-movein-{uid}";
            var processRequest = await stateMachine.CreateRequestAsync(ctx.Gsrn, "move_in", ed, ct);
            await stateMachine.MarkSentAsync(processRequest.Id, corrId, ct);
            ctx.ProcessRequestId = processRequest.Id;
            ctx.IsBrsSubmitted = true;
            executed.Add(new SimulationStep(2, "Submit BRS-009",
                $"Process {processRequest.Id:N} sent", currentDate));
        }

        // Step 3: DataHub Acknowledges
        if (ctx.IsBrsSubmitted && !ctx.IsAcknowledged && currentDate >= timeline.GetDate("DataHub Acknowledges"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            await stateMachine.MarkAcknowledgedAsync(ctx.ProcessRequestId, ct);
            ctx.IsAcknowledged = true;
            executed.Add(new SimulationStep(3, "DataHub Acknowledges",
                "Process acknowledged", currentDate));
        }

        // Step 4: Receive RSM-022 + Create Supply
        if (ctx.IsAcknowledged && !ctx.IsRsm022Received && currentDate >= timeline.GetDate("Receive RSM-022"))
        {
            var portfolio = new PortfolioRepository(_connectionString);
            var uid = Guid.NewGuid().ToString("N")[..8];
            var msgId022 = $"msg-rsm022-movein-{uid}";
            await using (var msgConn = new NpgsqlConnection(_connectionString))
            {
                await msgConn.OpenAsync(ct);
                await msgConn.ExecuteAsync("""
                    INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                    VALUES (@MsgId, 'RSM-022', @CorrId, 'MasterData', 'processed', 1024)
                    """, new { MsgId = msgId022, CorrId = $"corr-movein-{uid}" });
                await msgConn.ExecuteAsync("""
                    INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                    """, new { MsgId = msgId022 });
            }

            await using (var supplyConn = new NpgsqlConnection(_connectionString))
            {
                await supplyConn.OpenAsync(ct);
                await supplyConn.ExecuteAsync("""
                    INSERT INTO portfolio.supply_period (gsrn, start_date)
                    SELECT @Gsrn, @StartDate
                    WHERE NOT EXISTS (SELECT 1 FROM portfolio.supply_period WHERE gsrn = @Gsrn AND start_date = @StartDate)
                    """, new { Gsrn = ctx.Gsrn, StartDate = ed });
            }
            await portfolio.ActivateMeteringPointAsync(ctx.Gsrn, effectiveStart, ct);
            ctx.IsRsm022Received = true;
            executed.Add(new SimulationStep(4, "Receive RSM-022",
                "Supply period created, metering point activated", currentDate));
        }

        // Step 5: Effectuation + Complete
        if (ctx.IsRsm022Received && !ctx.IsEffectuated && currentDate >= timeline.GetDate("Effectuation"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            await stateMachine.MarkCompletedAsync(ctx.ProcessRequestId, ct);
            ctx.IsEffectuated = true;
            executed.Add(new SimulationStep(5, "Effectuation",
                "Supply begins, process completed", currentDate));
        }

        // Step 6: Receive RSM-012 (daily deliveries — each day delivers previous day's 24h)
        if (ctx.IsEffectuated && !ctx.IsMeteringReceived)
        {
            var firstDeliveryDate = ed.AddDays(1);
            var lastPossibleDelivery = ed.AddMonths(1).AddDays(2);
            var deliverUpTo = currentDate < lastPossibleDelivery ? currentDate : lastPossibleDelivery;

            if (deliverUpTo >= firstDeliveryDate && ctx.MeteringDaysDelivered < ctx.TotalMeteringDays)
            {
                var meteringRepo = new MeteringDataRepository(_connectionString);
                var daysToDeliver = deliverUpTo.DayNumber - firstDeliveryDate.DayNumber + 1;
                var targetDays = Math.Min(daysToDeliver, ctx.TotalMeteringDays);
                var newDays = targetDays - ctx.MeteringDaysDelivered;

                if (newDays > 0)
                {
                    var dayOffset = ctx.MeteringDaysDelivered;
                    var batchStart = effectiveStart.AddDays(dayOffset);
                    var batchHours = newDays * 24;
                    var uid = Guid.NewGuid().ToString("N")[..8];
                    var msgId012 = $"msg-rsm012-movein-{uid}";
                    var rows = GenerateMeteringData(batchStart, batchHours, 0.55m, msgId012);
                    await meteringRepo.StoreTimeSeriesAsync(ctx.Gsrn, rows, ct);

                    await using (var msgConn = new NpgsqlConnection(_connectionString))
                    {
                        await msgConn.OpenAsync(ct);
                        await msgConn.ExecuteAsync("""
                            INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                            VALUES (@MsgId, 'RSM-012', NULL, 'Timeseries', 'processed', @Size)
                            """, new { MsgId = msgId012, Size = batchHours * 70 });
                        await msgConn.ExecuteAsync("""
                            INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                            """, new { MsgId = msgId012 });
                    }

                    ctx.MeteringDaysDelivered = targetDays;
                    var totalKwh = targetDays * 24 * 0.55m;
                    var label = ctx.MeteringDaysDelivered >= ctx.TotalMeteringDays
                        ? $"All {ctx.TotalMeteringDays} days received ({totalKwh:N1} kWh)"
                        : $"Day {ctx.MeteringDaysDelivered}/{ctx.TotalMeteringDays} ({totalKwh:N1} kWh)";
                    executed.Add(new SimulationStep(6, "Receive RSM-012", label, currentDate));

                    if (ctx.MeteringDaysDelivered >= ctx.TotalMeteringDays)
                        ctx.IsMeteringReceived = true;
                }
            }
        }

        // Step 7: Run Settlement
        if (ctx.IsMeteringReceived && !ctx.IsSettled && currentDate >= timeline.GetDate("Run Settlement"))
        {
            var portfolio = new PortfolioRepository(_connectionString);
            var tariffRepo = new TariffRepository(_connectionString);
            var spotPriceRepo = new SpotPriceRepository(_connectionString);
            var meteringRepo = new MeteringDataRepository(_connectionString);

            var periodEnd = effectiveStart.AddMonths(1);

            var contract = await portfolio.GetActiveContractAsync(ctx.Gsrn, ct)
                ?? throw new InvalidOperationException($"No active contract for GSRN {ctx.Gsrn}");
            var product = await portfolio.GetProductAsync(contract.ProductId, ct)
                ?? throw new InvalidOperationException($"Product {contract.ProductId} not found");

            var consumption = await meteringRepo.GetConsumptionAsync(ctx.Gsrn, effectiveStart, periodEnd, ct);
            var spotPrices = await spotPriceRepo.GetPricesAsync("DK1", effectiveStart, periodEnd, ct);
            var midMonth = ed.AddDays(14);
            var rates = await tariffRepo.GetRatesAsync("344", "grid", midMonth, ct);
            var elTax = await tariffRepo.GetElectricityTaxAsync(midMonth, ct) ?? 0m;
            var gridSub = await tariffRepo.GetSubscriptionAsync("344", "grid", midMonth, ct) ?? 0m;

            var engine = new SettlementEngine();
            var result = engine.Calculate(new SettlementRequest(
                ctx.Gsrn,
                ed, DateOnly.FromDateTime(periodEnd),
                consumption, spotPrices,
                rates, 0.054m, 0.049m, elTax, gridSub,
                product.MarginOrePerKwh / 100m,
                (product.SupplementOrePerKwh ?? 0m) / 100m,
                product.SubscriptionKrPerMonth));

            await using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                await conn.ExecuteAsync("SELECT pg_advisory_lock(hashtext(@Key))", new { Key = ctx.Gsrn });
                try
                {
                    var billingPeriodId = await conn.QuerySingleAsync<Guid>("""
                        INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                        VALUES (@PeriodStart, @PeriodEnd, 'monthly')
                        ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
                        RETURNING id
                        """, new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd });

                    var settlementRunId = await conn.QuerySingleAsync<Guid>("""
                        INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, metering_points_count)
                        VALUES (
                            @BillingPeriodId, '344', @MeteringPointId,
                            COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                      WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                            'completed', 1)
                        RETURNING id
                        """, new { BillingPeriodId = billingPeriodId, MeteringPointId = ctx.Gsrn });

                    foreach (var line in result.Lines)
                    {
                        await conn.ExecuteAsync("""
                            INSERT INTO settlement.settlement_line (settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency)
                            VALUES (@RunId, @Gsrn, @ChargeType, @TotalKwh, @TotalAmount, @VatAmount, 'DKK')
                            """, new
                        {
                            RunId = settlementRunId,
                            Gsrn = ctx.Gsrn,
                            line.ChargeType,
                            TotalKwh = line.Kwh ?? 0m,
                            TotalAmount = line.Amount,
                            VatAmount = Math.Round(line.Amount * 0.25m, 2),
                        });
                    }
                }
                finally
                {
                    await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))", new { Key = ctx.Gsrn });
                }
            }

            ctx.IsSettled = true;
            executed.Add(new SimulationStep(7, "Run Settlement",
                $"Total: {result.Total:N2} DKK (subtotal {result.Subtotal:N2}, VAT {result.VatAmount:N2})", currentDate));
        }

        return executed;
    }

    public async Task<List<SimulationStep>> TickAcontoChangeOfSupplierAsync(
        AcontoChangeOfSupplierContext ctx, DateOnly currentDate, CancellationToken ct)
    {
        var executed = new List<SimulationStep>();
        var timeline = ctx.Timeline;
        var ed = ctx.EffectiveDate;
        var effectiveStart = ed.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Step 1: Seed Data
        if (!ctx.IsSeeded && currentDate >= timeline.GetDate("Seed Data"))
        {
            await _seedLock.WaitAsync(ct);
            try
            {
                await TickSeedAsync(ctx.Gsrn, ctx.CustomerName, ed, createSupplyPeriod: true, ct);
            }
            finally
            {
                _seedLock.Release();
            }

            ctx.IsSeeded = true;
            executed.Add(new SimulationStep(1, "Seed Data",
                $"Reference data + customer {ctx.CustomerName} + GSRN {ctx.Gsrn}", currentDate));
        }

        // Step 2: Submit BRS-001
        if (ctx.IsSeeded && !ctx.IsBrsSubmitted && currentDate >= timeline.GetDate("Submit BRS-001"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            var uid = Guid.NewGuid().ToString("N")[..8];
            var corrId = $"corr-aconto-{uid}";
            var processRequest = await stateMachine.CreateRequestAsync(ctx.Gsrn, "supplier_switch", ed, ct);
            await stateMachine.MarkSentAsync(processRequest.Id, corrId, ct);
            ctx.ProcessRequestId = processRequest.Id;
            ctx.IsBrsSubmitted = true;
            executed.Add(new SimulationStep(2, "Submit BRS-001",
                $"Process {processRequest.Id:N} sent", currentDate));
        }

        // Step 3: DataHub Acknowledges
        if (ctx.IsBrsSubmitted && !ctx.IsAcknowledged && currentDate >= timeline.GetDate("DataHub Acknowledges"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            await stateMachine.MarkAcknowledgedAsync(ctx.ProcessRequestId, ct);
            ctx.IsAcknowledged = true;
            executed.Add(new SimulationStep(3, "DataHub Acknowledges",
                "Process acknowledged", currentDate));
        }

        // Step 4: Receive RSM-022
        if (ctx.IsAcknowledged && !ctx.IsRsm022Received && currentDate >= timeline.GetDate("Receive RSM-022"))
        {
            var portfolio = new PortfolioRepository(_connectionString);
            var uid = Guid.NewGuid().ToString("N")[..8];
            var msgId022 = $"msg-rsm022-aconto-{uid}";
            await using (var msgConn = new NpgsqlConnection(_connectionString))
            {
                await msgConn.OpenAsync(ct);
                await msgConn.ExecuteAsync("""
                    INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                    VALUES (@MsgId, 'RSM-022', @CorrId, 'MasterData', 'processed', 1024)
                    """, new { MsgId = msgId022, CorrId = $"corr-aconto-{uid}" });
                await msgConn.ExecuteAsync("""
                    INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                    """, new { MsgId = msgId022 });
            }
            await portfolio.ActivateMeteringPointAsync(ctx.Gsrn, effectiveStart, ct);
            ctx.IsRsm022Received = true;
            executed.Add(new SimulationStep(4, "Receive RSM-022",
                "Metering point activated", currentDate));
        }

        // Step 5: Effectuation + Complete
        if (ctx.IsRsm022Received && !ctx.IsEffectuated && currentDate >= timeline.GetDate("Effectuation"))
        {
            var processRepo = new ProcessRepository(_connectionString);
            var stateMachine = new ProcessStateMachine(processRepo, _clock);
            await stateMachine.MarkCompletedAsync(ctx.ProcessRequestId, ct);
            ctx.IsEffectuated = true;
            executed.Add(new SimulationStep(5, "Effectuation",
                "Supply begins, process completed", currentDate));
        }

        // Step 6: Estimate Aconto (after effectuation — switch is now irreversible)
        if (ctx.IsEffectuated && !ctx.IsAcontoEstimated && currentDate >= timeline.GetDate("Estimate Aconto"))
        {
            var portfolio = new PortfolioRepository(_connectionString);
            var contract = await portfolio.GetActiveContractAsync(ctx.Gsrn, ct)
                ?? throw new InvalidOperationException($"No active contract for GSRN {ctx.Gsrn}");
            var product = await portfolio.GetProductAsync(contract.ProductId, ct)
                ?? throw new InvalidOperationException($"Product {contract.ProductId} not found");

            var expectedPrice = AcontoEstimator.CalculateExpectedPricePerKwh(
                averageSpotPriceOrePerKwh: 75m, marginOrePerKwh: product.MarginOrePerKwh,
                systemTariffRate: 0.054m, transmissionTariffRate: 0.049m,
                electricityTaxRate: 0.008m, averageGridTariffRate: 0.18m);
            var gridSubRate = 49.00m;
            var supplierSubRate = product.SubscriptionKrPerMonth;
            ctx.AcontoEstimate = AcontoEstimator.EstimateQuarterlyAmount(
                annualConsumptionKwh: 4000m, expectedPrice, gridSubRate, supplierSubRate);

            ctx.IsAcontoEstimated = true;
            executed.Add(new SimulationStep(6, "Estimate Aconto",
                $"Quarterly estimate: {ctx.AcontoEstimate:N2} DKK (4,000 kWh/year)", currentDate));
        }

        // Step 7: Send Invoice
        if (ctx.IsAcontoEstimated && !ctx.IsInvoiceSent && currentDate >= timeline.GetDate("Send Invoice"))
        {
            ctx.IsInvoiceSent = true;
            executed.Add(new SimulationStep(7, "Send Invoice",
                $"Aconto invoice of {ctx.AcontoEstimate:N2} DKK sent to customer", currentDate));
        }

        // Step 8: Receive RSM-012 (daily deliveries — starts after effectuation)
        if (ctx.IsEffectuated && !ctx.IsMeteringReceived)
        {
            var firstDeliveryDate = ed.AddDays(1);
            var lastPossibleDelivery = ed.AddMonths(1).AddDays(2);
            var deliverUpTo = currentDate < lastPossibleDelivery ? currentDate : lastPossibleDelivery;

            if (deliverUpTo >= firstDeliveryDate && ctx.MeteringDaysDelivered < ctx.TotalMeteringDays)
            {
                var meteringRepo = new MeteringDataRepository(_connectionString);
                var daysToDeliver = deliverUpTo.DayNumber - firstDeliveryDate.DayNumber + 1;
                var targetDays = Math.Min(daysToDeliver, ctx.TotalMeteringDays);
                var newDays = targetDays - ctx.MeteringDaysDelivered;

                if (newDays > 0)
                {
                    var dayOffset = ctx.MeteringDaysDelivered;
                    var batchStart = effectiveStart.AddDays(dayOffset);
                    var batchHours = newDays * 24;
                    var uid = Guid.NewGuid().ToString("N")[..8];
                    var msgId012 = $"msg-rsm012-aconto-{uid}";
                    var rows = GenerateMeteringData(batchStart, batchHours, 0.55m, msgId012);
                    await meteringRepo.StoreTimeSeriesAsync(ctx.Gsrn, rows, ct);

                    await using (var msgConn = new NpgsqlConnection(_connectionString))
                    {
                        await msgConn.OpenAsync(ct);
                        await msgConn.ExecuteAsync("""
                            INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size)
                            VALUES (@MsgId, 'RSM-012', NULL, 'Timeseries', 'processed', @Size)
                            """, new { MsgId = msgId012, Size = batchHours * 70 });
                        await msgConn.ExecuteAsync("""
                            INSERT INTO datahub.processed_message_id (message_id) VALUES (@MsgId)
                            """, new { MsgId = msgId012 });
                    }

                    ctx.MeteringDaysDelivered = targetDays;
                    var totalKwh = targetDays * 24 * 0.55m;
                    var label = ctx.MeteringDaysDelivered >= ctx.TotalMeteringDays
                        ? $"All {ctx.TotalMeteringDays} days received ({totalKwh:N1} kWh)"
                        : $"Day {ctx.MeteringDaysDelivered}/{ctx.TotalMeteringDays} ({totalKwh:N1} kWh)";
                    executed.Add(new SimulationStep(8, "Receive RSM-012", label, currentDate));

                    if (ctx.MeteringDaysDelivered >= ctx.TotalMeteringDays)
                        ctx.IsMeteringReceived = true;
                }
            }
        }

        // Step 9: Record Payment (after effectuation — direct debit collection)
        if (ctx.IsEffectuated && !ctx.IsAcontoPaid && currentDate >= timeline.GetDate("Record Payment"))
        {
            var acontoRepo = new AcontoPaymentRepository(_connectionString);
            var qStartDate = ed;
            var qEndDate = ed.AddMonths(3).AddDays(-1);
            await acontoRepo.RecordPaymentAsync(ctx.Gsrn, qStartDate, qEndDate, ctx.AcontoEstimate, ct);

            ctx.IsAcontoPaid = true;
            executed.Add(new SimulationStep(9, "Record Payment",
                $"Aconto payment of {ctx.AcontoEstimate:N2} DKK collected for {qStartDate}\u2013{qEndDate}", currentDate));
        }

        // Step 10: Aconto Settlement (recorded once payment received)
        if (ctx.IsAcontoPaid && !ctx.IsAcontoSettled && currentDate >= timeline.GetDate("Aconto Settlement"))
        {
            var periodEnd = ed.AddMonths(3);

            await using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                await conn.ExecuteAsync("SELECT pg_advisory_lock(hashtext(@Key))", new { Key = ctx.Gsrn });
                try
                {
                    var billingPeriodId = await conn.QuerySingleAsync<Guid>("""
                        INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                        VALUES (@PeriodStart, @PeriodEnd, 'quarterly')
                        ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'quarterly'
                        RETURNING id
                        """, new { PeriodStart = ed, PeriodEnd = periodEnd });

                    var settlementRunId = await conn.QuerySingleAsync<Guid>("""
                        INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, metering_points_count)
                        VALUES (
                            @BillingPeriodId, '344', @MeteringPointId,
                            COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                      WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                            'completed', 1)
                        RETURNING id
                        """, new { BillingPeriodId = billingPeriodId, MeteringPointId = ctx.Gsrn });

                    var vatAmount = Math.Round(ctx.AcontoEstimate * 0.25m / 1.25m, 2);
                    await conn.ExecuteAsync("""
                        INSERT INTO settlement.settlement_line (settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency)
                        VALUES (@RunId, @Gsrn, 'aconto', 0, @TotalAmount, @VatAmount, 'DKK')
                        """, new
                    {
                        RunId = settlementRunId,
                        Gsrn = ctx.Gsrn,
                        TotalAmount = ctx.AcontoEstimate,
                        VatAmount = vatAmount,
                    });
                }
                finally
                {
                    await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))", new { Key = ctx.Gsrn });
                }
            }

            ctx.IsAcontoSettled = true;
            executed.Add(new SimulationStep(10, "Aconto Settlement",
                $"Aconto settlement: {ctx.AcontoEstimate:N2} DKK (estimate-based)", currentDate));
        }

        return executed;
    }

    /// <summary>
    /// Gets all message audit entries for the CIM viewer.
    /// NOTE: This is a stub implementation. Message audit functionality is planned for V021 migration.
    /// </summary>
    public Task<IEnumerable<MessageAuditEntry>> GetMessageAuditListAsync(string? gsrn, string? direction, CancellationToken ct)
    {
        // Stub: message audit storage not yet implemented
        return Task.FromResult(Enumerable.Empty<MessageAuditEntry>());
    }

    /// <summary>
    /// Gets a specific message by GSRN and step name, returning the JSON payload.
    /// NOTE: This is a stub implementation. Message audit functionality is planned for V021 migration.
    /// </summary>
    public Task<string?> GetMessageAuditAsync(string gsrn, string stepName, CancellationToken ct)
    {
        // Stub: message audit storage not yet implemented
        return Task.FromResult<string?>(null);
    }
}

/// <summary>
/// Represents a CIM message audit entry.
/// NOTE: This is a stub type. Full implementation planned for V021 migration.
/// </summary>
public record MessageAuditEntry(
    Guid Id,
    DateTime CreatedAt,
    string Direction,
    string MessageType,
    string StepName,
    string? Gsrn,
    string RawPayload);
