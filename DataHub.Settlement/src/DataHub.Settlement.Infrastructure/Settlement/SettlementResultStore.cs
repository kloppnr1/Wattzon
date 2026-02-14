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
                    cancellationToken: ct));

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
        }
        finally
        {
            await conn.ExecuteAsync("SELECT pg_advisory_unlock(hashtext(@Key))",
                new { Key = $"{gsrn}:{result.PeriodStart}:{result.PeriodEnd}" });
        }
    }
}
