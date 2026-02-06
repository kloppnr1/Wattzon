using Dapper;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Dashboard;

public sealed class DashboardQueryService
{
    private readonly string _connectionString;

    static DashboardQueryService()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public DashboardQueryService(string connectionString)
    {
        _connectionString = connectionString;
    }

    // ── Dashboard page ──

    public async Task<DashboardCounts> GetDashboardCountsAsync()
    {
        const string sql = """
            SELECT
                (SELECT count(*) FROM portfolio.customer)::int AS customers,
                (SELECT count(*) FROM datahub.processed_message_id)::int AS messages_processed,
                (SELECT count(*) FROM lifecycle.process_request WHERE status NOT IN ('completed','cancelled','rejected'))::int AS active_processes,
                (SELECT count(*) FROM settlement.settlement_run)::int AS settlement_runs
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<DashboardCounts>(sql);
    }

    public async Task<IReadOnlyList<ActivityEntry>> GetRecentActivityAsync(int limit = 20)
    {
        const string sql = """
            SELECT pe.occurred_at AS timestamp, pe.event_type AS event,
                   COALESCE(pr.gsrn || ' — ' || pe.source, pe.source, '') AS details
            FROM lifecycle.process_event pe
            LEFT JOIN lifecycle.process_request pr ON pr.id = pe.process_request_id
            ORDER BY pe.occurred_at DESC
            LIMIT @Limit
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<ActivityEntry>(sql, new { Limit = limit });
        return rows.ToList();
    }

    // ── Messages page ──

    public async Task<MessageCounts> GetMessageCountsAsync()
    {
        const string sql = """
            SELECT
                (SELECT count(*) FROM datahub.inbound_message)::int AS total_received,
                (SELECT count(*) FROM datahub.processed_message_id)::int AS processed,
                (SELECT count(*) FROM datahub.dead_letter)::int AS dead_letters
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<MessageCounts>(sql);
    }

    public async Task<IReadOnlyList<MessageEntry>> GetMessagesAsync()
    {
        const string sql = """
            SELECT datahub_message_id AS message_id, message_type, queue_name AS queue,
                   status, received_at
            FROM datahub.inbound_message
            ORDER BY received_at DESC
            LIMIT 100
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<MessageEntry>(sql);
        return rows.ToList();
    }

    // ── Processes page ──

    public async Task<IReadOnlyList<ProcessEntry>> GetProcessesAsync()
    {
        const string sql = """
            SELECT pr.gsrn, pr.process_type, pr.status, pr.effective_date,
                   COALESCE(pr.updated_at, pr.created_at) AS last_updated
            FROM lifecycle.process_request pr
            ORDER BY COALESCE(pr.updated_at, pr.created_at) DESC
            LIMIT 100
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<ProcessEntry>(sql);
        return rows.ToList();
    }

    // ── Settlement page ──

    public async Task<IReadOnlyList<SettlementRunRow>> GetSettlementRunsAsync()
    {
        const string sql = """
            SELECT sr.id AS run_id, bp.period_start, bp.period_end,
                   sr.grid_area_code, sr.status, sr.executed_at
            FROM settlement.settlement_run sr
            JOIN settlement.billing_period bp ON bp.id = sr.billing_period_id
            ORDER BY sr.executed_at DESC
            LIMIT 50
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<SettlementRunRow>(sql);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<SettlementLineRow>> GetSettlementLinesAsync(Guid settlementRunId)
    {
        const string sql = """
            SELECT metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency
            FROM settlement.settlement_line
            WHERE settlement_run_id = @RunId
            ORDER BY id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<SettlementLineRow>(sql, new { RunId = settlementRunId });
        return rows.ToList();
    }

    public async Task<string?> GetCustomerNameForMeteringPointAsync(string gsrn)
    {
        const string sql = """
            SELECT c.name
            FROM portfolio.contract ct
            JOIN portfolio.customer c ON c.id = ct.customer_id
            WHERE ct.gsrn = @Gsrn AND ct.end_date IS NULL
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<string>(sql, new { Gsrn = gsrn });
    }

    // ── DTOs ──

    public record DashboardCounts(int Customers, int MessagesProcessed, int ActiveProcesses, int SettlementRuns);
    public record ActivityEntry(DateTime Timestamp, string Event, string Details);
    public record MessageCounts(int TotalReceived, int Processed, int DeadLetters);
    public record MessageEntry(string MessageId, string MessageType, string Queue, string Status, DateTime ReceivedAt);
    public record ProcessEntry(string Gsrn, string ProcessType, string Status, DateOnly EffectiveDate, DateTime LastUpdated);
    public record SettlementRunRow(Guid RunId, DateOnly PeriodStart, DateOnly PeriodEnd, string GridAreaCode, string Status, DateTime ExecutedAt);
    public record SettlementLineRow(string MeteringPointId, string ChargeType, decimal TotalKwh, decimal TotalAmount, decimal VatAmount, string Currency);
}
