using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Infrastructure.Settlement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Billing;

public sealed class InvoicingService : BackgroundService
{
    private readonly string _connectionString;
    private readonly IInvoiceService _invoiceService;
    private readonly IClock _clock;
    private readonly ILogger<InvoicingService> _logger;
    private readonly TimeSpan _pollInterval;

    public InvoicingService(
        string connectionString,
        IInvoiceService invoiceService,
        IClock clock,
        ILogger<InvoicingService> logger,
        TimeSpan? pollInterval = null)
    {
        _connectionString = connectionString;
        _invoiceService = invoiceService;
        _clock = clock;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Invoicing service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during invoicing tick");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        var dueRuns = await GetUninvoicedDueRunsAsync(ct);

        // Split into direct payment (invoice individually) and aconto (group by period boundary)
        var directRuns = dueRuns.Where(r => r.PaymentModel != "aconto").ToList();
        var acontoRuns = dueRuns.Where(r => r.PaymentModel == "aconto").ToList();

        // Direct payment: invoice each run individually
        foreach (var run in directRuns)
        {
            try
            {
                var lines = await GetSettlementLinesAsync(run.SettlementRunId, run.Gsrn, run.PeriodStart, run.PeriodEnd, ct);

                await _invoiceService.CreateInvoiceAsync(
                    run.CustomerId, run.PayerId, run.ContractId,
                    run.SettlementRunId, run.BillingPeriodId, run.Gsrn,
                    run.PeriodStart, run.PeriodEnd, lines, null, ct);

                _logger.LogInformation(
                    "Created invoice for settlement run {RunId}, GSRN {Gsrn}, period {Start}–{End}",
                    run.SettlementRunId, run.Gsrn, run.PeriodStart, run.PeriodEnd);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to create invoice for settlement run {RunId} — will retry next tick",
                    run.SettlementRunId);
            }
        }

        // Aconto: group runs by (gsrn, aconto period) and create combined invoice at period boundary
        var acontoGroups = acontoRuns
            .GroupBy(r => (r.Gsrn, AcontoPeriodEnd: GetAcontoPeriodEnd(r.PeriodStart, r.BillingFrequency)))
            .ToList();

        foreach (var group in acontoGroups)
        {
            var today = _clock.Today;
            var acontoPeriodEnd = group.Key.AcontoPeriodEnd;

            // Only invoice when aconto period has ended
            if (acontoPeriodEnd > today)
                continue;

            try
            {
                var runs = group.OrderBy(r => r.PeriodStart).ToList();
                var first = runs.First();
                var overallStart = runs.Min(r => r.PeriodStart);
                var overallEnd = runs.Max(r => r.PeriodEnd);

                // Aggregate settlement lines from all runs in this aconto period
                var allLines = new List<CreateInvoiceLineRequest>();
                foreach (var run in runs)
                {
                    var runLines = await GetSettlementLinesAsync(run.SettlementRunId, run.Gsrn, run.PeriodStart, run.PeriodEnd, ct);
                    allLines.AddRange(runLines);
                }

                // Re-number lines
                for (int i = 0; i < allLines.Count; i++)
                    allLines[i] = allLines[i] with { SortOrder = i + 1 };

                // Deduct total aconto prepayments from prior invoices for this period
                var totalAcontoPaid = await GetAcontoPrepaymentTotalAsync(first.Gsrn, overallStart, acontoPeriodEnd, ct);
                if (totalAcontoPaid > 0)
                {
                    allLines.Add(new CreateInvoiceLineRequest(
                        null, first.Gsrn, allLines.Count + 1, "aconto_deduction",
                        $"Aconto deduction — {overallStart:yyyy-MM-dd} to {acontoPeriodEnd:yyyy-MM-dd}",
                        0m, null, -totalAcontoPaid, 0m, -totalAcontoPaid));

                    _logger.LogInformation(
                        "Deducting {Amount} DKK aconto for GSRN {Gsrn}, aconto period {Start}–{End}",
                        totalAcontoPaid, first.Gsrn, overallStart, acontoPeriodEnd);
                }

                // Add new aconto prepayment line for next period (based on actual total)
                var actualTotal = allLines.Sum(l => l.AmountInclVat);
                if (actualTotal > 0)
                {
                    var nextPeriodEnd = GetAcontoPeriodEnd(acontoPeriodEnd, first.BillingFrequency);
                    var acontoPrepayment = Math.Round(actualTotal, 2);

                    allLines.Add(new CreateInvoiceLineRequest(
                        null, first.Gsrn, allLines.Count + 1, "aconto_prepayment",
                        $"Aconto prepayment — {acontoPeriodEnd:yyyy-MM-dd} to {nextPeriodEnd:yyyy-MM-dd}",
                        0m, null, acontoPrepayment, 0m, acontoPrepayment));

                    _logger.LogInformation(
                        "Added aconto prepayment {Amount} DKK for GSRN {Gsrn}, next period {Start}–{End}",
                        acontoPrepayment, first.Gsrn, acontoPeriodEnd, nextPeriodEnd);
                }

                // Create ONE invoice — lines determine what it is
                await _invoiceService.CreateInvoiceAsync(
                    first.CustomerId, first.PayerId, first.ContractId,
                    first.SettlementRunId, first.BillingPeriodId, first.Gsrn,
                    overallStart, overallEnd, allLines, null, ct);

                _logger.LogInformation(
                    "Created invoice for GSRN {Gsrn}, aconto period {Start}–{End}, {RunCount} settlement runs",
                    first.Gsrn, overallStart, acontoPeriodEnd, runs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to create invoice for GSRN {Gsrn} — will retry next tick",
                    group.Key.Gsrn);
            }
        }
    }

    /// <summary>
    /// Queries aconto prepayment totals from invoice lines (replaces shadow ledger).
    /// </summary>
    private async Task<decimal> GetAcontoPrepaymentTotalAsync(string gsrn, DateOnly from, DateOnly to, CancellationToken ct)
    {
        const string sql = """
            SELECT COALESCE(SUM(il.amount_ex_vat), 0)
            FROM billing.invoice_line il
            JOIN billing.invoice i ON i.id = il.invoice_id
            WHERE il.gsrn = @Gsrn
              AND il.line_type = 'aconto_prepayment'
              AND i.period_start >= @From AND i.period_end <= @To
              AND i.status NOT IN ('cancelled', 'credited')
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<decimal>(
            new CommandDefinition(sql, new { Gsrn = gsrn, From = from, To = to }, cancellationToken: ct));
    }

    internal async Task<IReadOnlyList<UninvoicedRun>> GetUninvoicedDueRunsAsync(CancellationToken ct)
    {
        var today = _clock.Today;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var runs = await conn.QueryAsync<UninvoicedRun>(
            new CommandDefinition("""
                SELECT
                    sr.id AS settlement_run_id,
                    sr.billing_period_id,
                    sr.metering_point_id AS gsrn,
                    bp.period_start,
                    bp.period_end,
                    c.customer_id,
                    c.payer_id,
                    c.id AS contract_id,
                    c.billing_frequency,
                    c.payment_model
                FROM settlement.settlement_run sr
                JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
                JOIN portfolio.contract c ON c.gsrn = sr.metering_point_id
                    AND c.end_date IS NULL
                WHERE sr.status = 'completed'
                  AND NOT EXISTS (
                      SELECT 1 FROM billing.invoice i
                      WHERE i.settlement_run_id = sr.id
                        AND i.status <> 'cancelled'
                  )
                """,
                cancellationToken: ct));

        return runs
            .Where(r => IsPeriodDue(r.BillingFrequency, r.PeriodEnd, today))
            .ToList();
    }

    internal static bool IsPeriodDue(string billingFrequency, DateOnly periodEnd, DateOnly today)
    {
        if (billingFrequency == "quarterly")
        {
            // periodEnd is exclusive (day after last day), so subtract 1 to get the last actual day
            // to determine which quarter the period belongs to.
            var lastDay = periodEnd.AddDays(-1);
            var quarterEnd = GetQuarterEnd(lastDay);
            return today > quarterEnd;
        }

        // Weekly, daily, and monthly: due once the period has ended (periodEnd is exclusive)
        return periodEnd <= today;
    }

    /// <summary>
    /// Returns the exclusive end date of the aconto period containing the given date.
    /// Monthly → first of next month. Quarterly → first of next quarter.
    /// </summary>
    internal static DateOnly GetAcontoPeriodEnd(DateOnly date, string acontoFrequency)
        => BillingPeriodCalculator.GetFirstPeriodEnd(date, acontoFrequency);

    private static DateOnly GetQuarterEnd(DateOnly date)
    {
        var quarterMonth = ((date.Month - 1) / 3 + 1) * 3;
        return new DateOnly(date.Year, quarterMonth, DateTime.DaysInMonth(date.Year, quarterMonth));
    }

    private async Task<IReadOnlyList<CreateInvoiceLineRequest>> GetSettlementLinesAsync(
        Guid settlementRunId, string gsrn, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var lines = await conn.QueryAsync<SettlementLineRow>(
            new CommandDefinition("""
                SELECT id, charge_type, total_kwh, total_amount, vat_amount
                FROM settlement.settlement_line
                WHERE settlement_run_id = @RunId
                ORDER BY id
                """,
                new { RunId = settlementRunId },
                cancellationToken: ct));

        var sortOrder = 1;
        return lines.Select(l => new CreateInvoiceLineRequest(
            l.Id, gsrn, sortOrder++, l.ChargeType,
            $"{l.ChargeType.Replace('_', ' ')} — {periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}",
            l.TotalKwh, null, l.TotalAmount, l.VatAmount, l.TotalAmount + l.VatAmount
        )).ToList();
    }

    internal record UninvoicedRun(
        Guid SettlementRunId,
        Guid BillingPeriodId,
        string Gsrn,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        Guid CustomerId,
        Guid? PayerId,
        Guid ContractId,
        string BillingFrequency,
        string PaymentModel);

    private record SettlementLineRow(Guid Id, string ChargeType, decimal TotalKwh, decimal TotalAmount, decimal VatAmount);
}
