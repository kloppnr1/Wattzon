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
            RETURNING id, process_type, gsrn, status, effective_date, datahub_correlation_id, cancel_correlation_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<ProcessRequest>(
            new CommandDefinition(sql, new { ProcessType = processType, Gsrn = gsrn, EffectiveDate = effectiveDate }, cancellationToken: ct));
    }

    public async Task<ProcessRequest> CreateWithEventAsync(string processType, string gsrn, DateOnly effectiveDate, CancellationToken ct)
    {
        const string insertProcess = """
            INSERT INTO lifecycle.process_request (process_type, gsrn, effective_date)
            VALUES (@ProcessType, @Gsrn, @EffectiveDate)
            RETURNING id, process_type, gsrn, status, effective_date, datahub_correlation_id, cancel_correlation_id
            """;

        const string insertEvent = """
            INSERT INTO lifecycle.process_event (process_request_id, event_type, source)
            VALUES (@ProcessRequestId, 'created', 'system')
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var request = await conn.QuerySingleAsync<ProcessRequest>(
                new CommandDefinition(insertProcess, new { ProcessType = processType, Gsrn = gsrn, EffectiveDate = effectiveDate },
                    transaction: tx, cancellationToken: ct));

            await conn.ExecuteAsync(
                new CommandDefinition(insertEvent, new { ProcessRequestId = request.Id },
                    transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return request;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException($"A process is already active for GSRN {gsrn}.", ex);
        }
    }

    public async Task<ProcessRequest?> GetAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id, cancel_correlation_id
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
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id, cancel_correlation_id
            FROM lifecycle.process_request
            WHERE datahub_correlation_id = @CorrelationId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProcessRequest>(
            new CommandDefinition(sql, new { CorrelationId = correlationId }, cancellationToken: ct));
    }

    public async Task<ProcessRequest?> GetByCancelCorrelationIdAsync(string cancelCorrelationId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id, cancel_correlation_id
            FROM lifecycle.process_request
            WHERE cancel_correlation_id = @CancelCorrelationId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProcessRequest>(
            new CommandDefinition(sql, new { CancelCorrelationId = cancelCorrelationId }, cancellationToken: ct));
    }

    public async Task SetCancelCorrelationIdAsync(Guid id, string cancelCorrelationId, CancellationToken ct)
    {
        const string sql = """
            UPDATE lifecycle.process_request
            SET cancel_correlation_id = @CancelCorrelationId, updated_at = now()
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, CancelCorrelationId = cancelCorrelationId }, cancellationToken: ct));
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

    public async Task TransitionWithEventAsync(Guid id, string newStatus, string expectedStatus, string? correlationId, string eventType, CancellationToken ct)
    {
        var updateSql = correlationId is not null
            ? """
              UPDATE lifecycle.process_request
              SET status = @NewStatus, datahub_correlation_id = @CorrelationId, updated_at = now()
              WHERE id = @Id AND status = @ExpectedStatus
              """
            : """
              UPDATE lifecycle.process_request
              SET status = @NewStatus, updated_at = now()
              WHERE id = @Id AND status = @ExpectedStatus
              """;

        const string insertEvent = """
            INSERT INTO lifecycle.process_event (process_request_id, event_type, source)
            VALUES (@ProcessRequestId, @EventType, 'system')
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rows = await conn.ExecuteAsync(
            new CommandDefinition(updateSql,
                new { Id = id, NewStatus = newStatus, ExpectedStatus = expectedStatus, CorrelationId = correlationId },
                transaction: tx, cancellationToken: ct));

        if (rows == 0)
        {
            throw new InvalidOperationException(
                $"Concurrency conflict: process {id} is no longer in status '{expectedStatus}'.");
        }

        await conn.ExecuteAsync(
            new CommandDefinition(insertEvent, new { ProcessRequestId = id, EventType = eventType },
                transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
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

    public async Task<bool> HasActiveByGsrnAsync(string gsrn, CancellationToken ct)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM lifecycle.process_request
                WHERE gsrn = @Gsrn
                AND status NOT IN ('completed','cancelled','rejected','final_settled')
            )
            """;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<bool>(
            new CommandDefinition(sql, new { Gsrn = gsrn }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProcessRequest>> GetByStatusAsync(string status, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id, cancel_correlation_id
            FROM lifecycle.process_request
            WHERE status = @Status
            ORDER BY effective_date
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProcessRequest>(
            new CommandDefinition(sql, new { Status = status }, cancellationToken: ct));
        return rows.ToList();
    }
}
