using Dapper;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Dashboard;

public record ValidationResult(bool IsValid, string? ErrorMessage);

public static class MarketRules
{
    private static readonly ValidationResult Valid = new(true, null);

    public static async Task<ValidationResult> CanChangeSupplierAsync(string gsrn, string connString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct);

        var hasActiveSupply = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.supply_period WHERE gsrn = @Gsrn AND end_date IS NULL)",
            new { Gsrn = gsrn });

        if (hasActiveSupply)
            return new ValidationResult(false, $"Already supplying GSRN {gsrn}");

        var hasPendingProcess = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM lifecycle.process_request WHERE gsrn = @Gsrn AND status NOT IN ('completed','cancelled','rejected','final_settled'))",
            new { Gsrn = gsrn });

        if (hasPendingProcess)
            return new ValidationResult(false, $"Conflicting process in progress for GSRN {gsrn}");

        return Valid;
    }

    public static async Task<ValidationResult> CanReceiveMeteringAsync(string gsrn, string connString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct);

        var hasActiveSupply = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.supply_period WHERE gsrn = @Gsrn AND end_date IS NULL)",
            new { Gsrn = gsrn });

        if (!hasActiveSupply)
            return new ValidationResult(false, $"No active supply period for GSRN {gsrn}");

        var isActivated = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.metering_point WHERE gsrn = @Gsrn AND activated_at IS NOT NULL AND deactivated_at IS NULL)",
            new { Gsrn = gsrn });

        if (!isActivated)
            return new ValidationResult(false, $"Metering point {gsrn} is not activated");

        return Valid;
    }

    public static async Task<ValidationResult> CanRunSettlementAsync(string gsrn, string connString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct);

        var hasActiveContract = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.contract WHERE gsrn = @Gsrn AND end_date IS NULL)",
            new { Gsrn = gsrn });

        if (!hasActiveContract)
            return new ValidationResult(false, $"No active contract for GSRN {gsrn}");

        var hasUnsettledData = await conn.QuerySingleAsync<bool>("""
            SELECT EXISTS(
                SELECT 1 FROM metering.metering_data md
                WHERE md.metering_point_id = @Gsrn
                  AND md.timestamp >= COALESCE(
                    (SELECT MAX(bp.period_end)
                     FROM settlement.settlement_line sl
                     JOIN settlement.settlement_run sr ON sr.id = sl.settlement_run_id
                     JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
                     WHERE sl.metering_point_id = @Gsrn),
                    '1970-01-01'::timestamptz))
            """, new { Gsrn = gsrn });

        if (!hasUnsettledData)
            return new ValidationResult(false, $"No unsettled metering data for GSRN {gsrn}");

        return Valid;
    }

    public static async Task<ValidationResult> CanOffboardAsync(string gsrn, string connString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct);

        var hasActiveSupply = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.supply_period WHERE gsrn = @Gsrn AND end_date IS NULL)",
            new { Gsrn = gsrn });

        if (!hasActiveSupply)
            return new ValidationResult(false, $"No active supply period for GSRN {gsrn}");

        var hasCompletedProcess = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM lifecycle.process_request WHERE gsrn = @Gsrn AND status = 'completed')",
            new { Gsrn = gsrn });

        if (!hasCompletedProcess)
            return new ValidationResult(false, $"No completed process for GSRN {gsrn} — cannot offboard");

        return Valid;
    }

    public static async Task<ValidationResult> CanBillAcontoAsync(string gsrn, string connString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct);

        var hasActiveSupply = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.supply_period WHERE gsrn = @Gsrn AND end_date IS NULL)",
            new { Gsrn = gsrn });

        if (!hasActiveSupply)
            return new ValidationResult(false, $"No active supply period for GSRN {gsrn}");

        var hasActiveContract = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolio.contract WHERE gsrn = @Gsrn AND end_date IS NULL)",
            new { Gsrn = gsrn });

        if (!hasActiveContract)
            return new ValidationResult(false, $"No active contract for GSRN {gsrn}");

        return Valid;
    }
}
