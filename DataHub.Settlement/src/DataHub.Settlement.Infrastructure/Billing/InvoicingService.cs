using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Billing;

public sealed class InvoicingService : BackgroundService
{
    private readonly string _connectionString;
    private readonly IInvoiceService _invoiceService;
    private readonly IAcontoPaymentRepository _acontoRepo;
    private readonly IClock _clock;
    private readonly ILogger<InvoicingService> _logger;
    private readonly TimeSpan _pollInterval;

    public InvoicingService(
        string connectionString,
        IInvoiceService invoiceService,
        IAcontoPaymentRepository acontoRepo,
        IClock clock,
        ILogger<InvoicingService> logger,
        TimeSpan? pollInterval = null)
    {
        _connectionString = connectionString;
        _invoiceService = invoiceService;
        _acontoRepo = acontoRepo;
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

        foreach (var run in dueRuns)
        {
            try
            {
                var lines = await GetSettlementLinesAsync(run.SettlementRunId, run.Gsrn, run.PeriodStart, run.PeriodEnd, ct);

                // For aconto customers, deduct prepaid amount from the settlement invoice
                if (run.PaymentModel == "aconto")
                {
                    var totalAcontoPaid = await _acontoRepo.GetTotalPaidAsync(run.Gsrn, run.PeriodStart, run.PeriodEnd, ct);
                    if (totalAcontoPaid > 0)
                    {
                        var deductionLine = new CreateInvoiceLineRequest(
                            null, run.Gsrn, lines.Count + 1, "aconto_deduction",
                            $"Aconto deduction — {run.PeriodStart:yyyy-MM-dd} to {run.PeriodEnd:yyyy-MM-dd}",
                            0m, null, -totalAcontoPaid, 0m, -totalAcontoPaid);
                        lines = lines.Append(deductionLine).ToList();

                        _logger.LogInformation(
                            "Deducting {Amount} DKK aconto for GSRN {Gsrn}, period {Start}–{End}",
                            totalAcontoPaid, run.Gsrn, run.PeriodStart, run.PeriodEnd);
                    }
                }

                await _invoiceService.CreateSettlementInvoiceAsync(
                    run.CustomerId, run.PayerId, run.ContractId,
                    run.SettlementRunId, run.BillingPeriodId, run.Gsrn,
                    run.PeriodStart, run.PeriodEnd, lines, ct);

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

        // Weekly and monthly: due once the period has ended (periodEnd is exclusive)
        return periodEnd <= today;
    }

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
