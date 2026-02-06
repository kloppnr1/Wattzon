using Dapper;
using DataHub.Settlement.Application.Lifecycle;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Lifecycle;

public sealed class ProcessRepository : IProcessRepository
{
    private readonly string _connectionString;

    static ProcessRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Database.DapperTypeHandlers.Register();
    }

    public ProcessRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ProcessRequest> CreateAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO lifecycle.process_request (process_type, gsrn, effective_date)
            VALUES (@ProcessType, @Gsrn, @EffectiveDate)
            RETURNING id, process_type, gsrn, status, effective_date, datahub_correlation_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<ProcessRequest>(
            new CommandDefinition(sql, new { ProcessType = processType, Gsrn = gsrn, EffectiveDate = effectiveDate }, cancellationToken: ct));
    }

    public async Task<ProcessRequest?> GetAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id
            FROM lifecycle.process_request
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProcessRequest>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<ProcessRequest?> GetByCorrelationIdAsync(string correlationId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id
            FROM lifecycle.process_request
            WHERE datahub_correlation_id = @CorrelationId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProcessRequest>(
            new CommandDefinition(sql, new { CorrelationId = correlationId }, cancellationToken: ct));
    }

    public async Task UpdateStatusAsync(Guid id, string status, string? correlationId, CancellationToken ct)
    {
        var sql = correlationId is not null
            ? """
              UPDATE lifecycle.process_request
              SET status = @Status, datahub_correlation_id = @CorrelationId, updated_at = now()
              WHERE id = @Id
              """
            : """
              UPDATE lifecycle.process_request
              SET status = @Status, updated_at = now()
              WHERE id = @Id
              """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, Status = status, CorrelationId = correlationId }, cancellationToken: ct));
    }

    public async Task AddEventAsync(Guid processRequestId, string eventType, string? payload, string? source, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO lifecycle.process_event (process_request_id, event_type, payload, source)
            VALUES (@ProcessRequestId, @EventType, CASE WHEN @Payload IS NULL THEN NULL ELSE jsonb_build_object('reason', @Payload) END, @Source)
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { ProcessRequestId = processRequestId, EventType = eventType, Payload = payload, Source = source }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProcessEvent>> GetEventsAsync(Guid processRequestId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_request_id, occurred_at, event_type, payload, source
            FROM lifecycle.process_event
            WHERE process_request_id = @ProcessRequestId
            ORDER BY occurred_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProcessEvent>(
            new CommandDefinition(sql, new { ProcessRequestId = processRequestId }, cancellationToken: ct));
        return rows.ToList();
    }
}
