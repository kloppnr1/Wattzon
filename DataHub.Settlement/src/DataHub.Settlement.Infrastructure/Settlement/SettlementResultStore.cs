using System;
using Dapper;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class SettlementResultStore : ISettlementResultStore
{
    private readonly string _connectionString;

    static SettlementResultStore()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public SettlementResultStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task StoreAsync(string gsrn, string gridAreaCode, SettlementResult result, string billingFrequency, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Advisory lock to prevent concurrent settlement runs for the same period
        await conn.ExecuteAsync("SELECT pg_advisory_lock(hashtext(@Key))",
            new { Key = $"{gsrn}:{result.PeriodStart}:{result.PeriodEnd}" });

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var billingPeriodId = await conn.QuerySingleAsync<Guid>(
                new CommandDefinition("""
                    INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                    VALUES (@PeriodStart, @PeriodEnd, @Frequency)
                    ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = EXCLUDED.frequency
                    RETURNING id
                    """,
                    new { PeriodStart = result.PeriodStart, PeriodEnd = result.PeriodEnd, Frequency = billingFrequency },
                    transaction: tx, cancellationToken: ct));

            var settlementRunId = await conn.QuerySingleAsync<Guid>(
                new CommandDefinition("""
                    INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, metering_points_count)
                    VALUES (
                        @BillingPeriodId, @GridAreaCode, @MeteringPointId,
                        COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                  WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                        'completed', 1)
                    RETURNING id
                    """,
                    new { BillingPeriodId = billingPeriodId, GridAreaCode = gridAreaCode, MeteringPointId = gsrn },
                    transaction: tx, cancellationToken: ct));

            // Allocate total VAT proportionally across lines so SUM(per-line VAT) = result.VatAmount exactly.
            // Without this, independently rounding each line's VAT causes off-by-penny discrepancies.
            var lineVatAmounts = AllocateVatToLines(result.Lines, result.Subtotal, result.VatAmount);

            for (var i = 0; i < result.Lines.Count; i++)
            {
                var line = result.Lines[i];
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
                            VatAmount = lineVatAmounts[i],
                        },
                        transaction: tx, cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))",
                new { Key = $"{gsrn}:{result.PeriodStart}:{result.PeriodEnd}" });
        }
    }

    public async Task<bool> HasSettlementRunAsync(string gsrn, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition("""
                SELECT EXISTS(
                    SELECT 1 FROM settlement.settlement_run sr
                    JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
                    WHERE sr.metering_point_id = @Gsrn
                    AND bp.period_start = @PeriodStart
                    AND bp.period_end = @PeriodEnd
                )
                """,
                new { Gsrn = gsrn, PeriodStart = periodStart, PeriodEnd = periodEnd },
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AffectedSettlementPeriod>> GetAffectedSettlementPeriodsAsync(
        string gsrn, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        const string sql = """
            SELECT sr.id AS settlement_run_id, bp.period_start, bp.period_end
            FROM settlement.settlement_run sr
            JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
            WHERE sr.metering_point_id = @Gsrn
              AND bp.period_start < @ToDate
              AND bp.period_end > @FromDate
            ORDER BY bp.period_start
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<AffectedPeriodRow>(
            new CommandDefinition(sql,
                new
                {
                    Gsrn = gsrn,
                    FromDate = DateOnly.FromDateTime(fromUtc.Date),
                    ToDate = DateOnly.FromDateTime(toUtc.Date).AddDays(1),
                },
                cancellationToken: ct));

        return rows.Select(r => new AffectedSettlementPeriod(
            r.SettlementRunId,
            DateOnly.FromDateTime(r.PeriodStart),
            DateOnly.FromDateTime(r.PeriodEnd))).ToList();
    }

    /// <summary>
    /// Allocates total VAT proportionally across settlement lines, ensuring the sum equals totalVat exactly.
    /// The last line absorbs any rounding remainder to prevent off-by-penny discrepancies.
    /// </summary>
    private static decimal[] AllocateVatToLines(IReadOnlyList<SettlementLine> lines, decimal subtotal, decimal totalVat)
    {
        var vatAmounts = new decimal[lines.Count];

        if (subtotal == 0m || lines.Count == 0)
        {
            for (var i = 0; i < lines.Count; i++)
                vatAmounts[i] = 0m;
            return vatAmounts;
        }

        var allocated = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            if (i == lines.Count - 1)
            {
                vatAmounts[i] = totalVat - allocated;
            }
            else
            {
                vatAmounts[i] = Math.Round(lines[i].Amount / subtotal * totalVat, 2);
                allocated += vatAmounts[i];
            }
        }

        return vatAmounts;
    }

    public async Task StoreFailedRunAsync(string gsrn, string gridAreaCode, DateOnly periodStart, DateOnly periodEnd, string errorDetails, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // Create or find the billing period
            var billingPeriodId = await conn.QuerySingleAsync<Guid>(
                new CommandDefinition("""
                    INSERT INTO settlement.billing_period (period_start, period_end, frequency)
                    VALUES (@PeriodStart, @PeriodEnd, 'monthly')
                    ON CONFLICT (period_start, period_end) DO UPDATE SET frequency = EXCLUDED.frequency
                    RETURNING id
                    """,
                    new { PeriodStart = periodStart, PeriodEnd = periodEnd },
                    transaction: tx, cancellationToken: ct));

            // Insert failed settlement run
            await conn.ExecuteAsync(
                new CommandDefinition("""
                    INSERT INTO settlement.settlement_run (billing_period_id, grid_area_code, metering_point_id, version, status, executed_at, error_details, metering_points_count)
                    VALUES (
                        @BillingPeriodId, @GridAreaCode, @MeteringPointId,
                        COALESCE((SELECT MAX(version) FROM settlement.settlement_run
                                  WHERE metering_point_id = @MeteringPointId AND billing_period_id = @BillingPeriodId), 0) + 1,
                        'failed', @ExecutedAt, @ErrorDetails, 1)
                    """,
                    new 
                    { 
                        BillingPeriodId = billingPeriodId, 
                        GridAreaCode = gridAreaCode, 
                        MeteringPointId = gsrn, 
                        ExecutedAt = DateTime.UtcNow,
                        ErrorDetails = errorDetails
                    },
                    transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}

internal class AffectedPeriodRow
{
    public Guid SettlementRunId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}
