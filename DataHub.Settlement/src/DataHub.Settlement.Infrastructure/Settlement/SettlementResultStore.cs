using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class SettlementResultStore : ISettlementResultStore
{
    private readonly string _connectionString;
    private readonly IInvoiceService? _invoiceService;
    private readonly IPortfolioRepository? _portfolioRepo;
    private readonly ILogger<SettlementResultStore>? _logger;

    static SettlementResultStore()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public SettlementResultStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SettlementResultStore(
        string connectionString,
        IInvoiceService invoiceService,
        IPortfolioRepository portfolioRepo,
        ILogger<SettlementResultStore> logger)
    {
        _connectionString = connectionString;
        _invoiceService = invoiceService;
        _portfolioRepo = portfolioRepo;
        _logger = logger;
    }

    public async Task StoreAsync(string gsrn, string gridAreaCode, SettlementResult result, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Advisory lock to prevent concurrent settlement runs for the same period
        await conn.ExecuteAsync("SELECT pg_advisory_lock(hashtext(@Key))",
            new { Key = $"{gsrn}:{result.PeriodStart}:{result.PeriodEnd}" });

        try
        {
            var billingPeriodId = await conn.QuerySingleAsync<Guid>(
                new CommandDefinition("""
                    INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                    VALUES (@PeriodStart, @PeriodEnd, 'monthly')
                    ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = 'monthly'
                    RETURNING id
                    """,
                    new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd },
                    cancellationToken: ct));

            var settlementRunId = await conn.QuerySingleAsync<Guid>(
                new CommandDefinition("""
                    INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, version, status, metering_points_count)
                    VALUES (@BillingPeriodId, @GridAreaCode, 1, 'completed', 1)
                    RETURNING id
                    """,
                    new { BillingPeriodId = billingPeriodId, GridAreaCode = gridAreaCode },
                    cancellationToken: ct));

            foreach (var line in result.Lines)
            {
                await conn.ExecuteAsync(
                    new CommandDefinition("""
                        INSERT INTO settlement.settlement_line (settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency)
                        VALUES (@RunId, @Gsrn, @ChargeType, @TotalKwh, @TotalAmount, @VatAmount, 'DKK')
                        """,
                        new
                        {
                            RunId = settlementRunId,
                            Gsrn = gsrn,
                            line.ChargeType,
                            TotalKwh = line.Kwh ?? 0m,
                            TotalAmount = line.Amount,
                            VatAmount = Math.Round(line.Amount * 0.25m, 2),
                        },
                        cancellationToken: ct));
            }

            // Create settlement invoice if service is available
            if (_invoiceService is not null && _portfolioRepo is not null)
            {
                try
                {
                    var contract = await _portfolioRepo.GetActiveContractAsync(gsrn, ct);
                    if (contract is not null)
                    {
                        var invoiceLines = new List<CreateInvoiceLineRequest>();
                        var sortOrder = 1;
                        foreach (var line in result.Lines)
                        {
                            var vatAmt = Math.Round(line.Amount * 0.25m, 2);
                            invoiceLines.Add(new CreateInvoiceLineRequest(
                                null, gsrn, sortOrder++, line.ChargeType,
                                $"{line.ChargeType.Replace('_', ' ')} — {result.PeriodStart:yyyy-MM-dd} to {result.PeriodEnd:yyyy-MM-dd}",
                                line.Kwh, null, line.Amount, vatAmt, line.Amount + vatAmt));
                        }

                        await _invoiceService.CreateSettlementInvoiceAsync(
                            contract.CustomerId, contract.PayerId, contract.Id,
                            settlementRunId, billingPeriodId, gsrn,
                            result.PeriodStart, result.PeriodEnd, invoiceLines, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex,
                        "Failed to create settlement invoice for GSRN {Gsrn} — invoice creation is non-blocking", gsrn);
                }
            }
        }
        finally
        {
            await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))",
                new { Key = $"{gsrn}:{result.PeriodStart}:{result.PeriodEnd}" });
        }
    }
}
