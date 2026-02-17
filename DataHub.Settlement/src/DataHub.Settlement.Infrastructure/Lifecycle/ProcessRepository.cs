using Dapper;
using DataHub.Settlement.Application.Common;
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
            RETURNING id, process_type, gsrn, status, effective_date, datahub_correlation_id, customer_data_received, tariff_data_received
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
            RETURNING id, process_type, gsrn, status, effective_date, datahub_correlation_id, customer_data_received, tariff_data_received
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
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id, customer_data_received, tariff_data_received
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
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id, customer_data_received, tariff_data_received
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

    public async Task AutoCancelAsync(Guid requestId, string expectedStatus, string reason, CancellationToken ct)
    {
        // Atomically: transition to 'cancelled' + add auto_cancelled event + add reason event
        // All in a single transaction to prevent partial state (stuck in cancellation_pending).
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rows = await conn.ExecuteAsync(
            new CommandDefinition("""
                UPDATE lifecycle.process_request
                SET status = 'cancelled', updated_at = now()
                WHERE id = @Id AND status = @ExpectedStatus
                """,
                new { Id = requestId, ExpectedStatus = expectedStatus },
                transaction: tx, cancellationToken: ct));

        if (rows == 0)
            throw new InvalidOperationException(
                $"Cannot auto-cancel process {requestId}: not in expected status '{expectedStatus}'.");

        await conn.ExecuteAsync(
            new CommandDefinition("""
                INSERT INTO lifecycle.process_event (process_request_id, event_type, source)
                VALUES (@Id, 'auto_cancelled', 'datahub')
                """,
                new { Id = requestId },
                transaction: tx, cancellationToken: ct));

        await conn.ExecuteAsync(
            new CommandDefinition("""
                INSERT INTO lifecycle.process_event (process_request_id, event_type, payload, source)
                VALUES (@Id, 'cancellation_reason', jsonb_build_object('reason', @Reason), 'datahub')
                """,
                new { Id = requestId, Reason = reason },
                transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ProcessRequest>> GetByStatusAsync(string status, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id, customer_data_received, tariff_data_received
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

    public async Task MarkCustomerDataReceivedAsync(string correlationId, CancellationToken ct)
    {
        const string sql = """
            UPDATE lifecycle.process_request
            SET customer_data_received = true, updated_at = now()
            WHERE datahub_correlation_id = @CorrelationId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CorrelationId = correlationId }, cancellationToken: ct));
    }

    public async Task MarkTariffDataReceivedAsync(string correlationId, CancellationToken ct)
    {
        const string sql = """
            UPDATE lifecycle.process_request
            SET tariff_data_received = true, updated_at = now()
            WHERE datahub_correlation_id = @CorrelationId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CorrelationId = correlationId }, cancellationToken: ct));
    }

    public async Task<ProcessDetail?> GetDetailWithChecklistAsync(Guid id, CancellationToken ct)
    {
        const string processSql = """
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id,
                   customer_data_received, tariff_data_received, created_at, updated_at
            FROM lifecycle.process_request
            WHERE id = @Id
            """;

        const string receivedSql = """
            SELECT message_type, status, MIN(received_at) AS received_at
            FROM datahub.inbound_message
            WHERE correlation_id = @CorrelationId
            GROUP BY message_type, status
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<ProcessDetailRow>(
            new CommandDefinition(processSql, new { Id = id }, cancellationToken: ct));

        if (row is null) return null;

        var expectedTypes = ProcessExpectedMessages.For(row.ProcessType);

        // If no correlation ID yet, all expected messages are pending
        var receivedMap = new Dictionary<string, ReceivedMessageRow>();
        if (row.DatahubCorrelationId is not null)
        {
            var received = await conn.QueryAsync<ReceivedMessageRow>(
                new CommandDefinition(receivedSql, new { CorrelationId = row.DatahubCorrelationId }, cancellationToken: ct));
            foreach (var r in received)
                receivedMap[r.MessageType] = r;
        }

        var checklist = expectedTypes.Select(mt =>
        {
            var found = receivedMap.GetValueOrDefault(mt);
            return new ExpectedMessageItem(
                mt,
                Received: found is not null,
                ReceivedAt: found?.ReceivedAt,
                Status: found?.Status);
        }).ToList();

        return new ProcessDetail(
            row.Id, row.ProcessType, row.Gsrn, row.Status,
            row.EffectiveDate, row.DatahubCorrelationId,
            row.CustomerDataReceived, row.TariffDataReceived,
            row.CreatedAt, row.UpdatedAt,
            checklist);
    }

    public async Task<IReadOnlyList<ProcessRequest>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct)
    {
        const string sql = """
            SELECT pr.id, pr.process_type, pr.gsrn, pr.status, pr.effective_date,
                   pr.datahub_correlation_id, pr.customer_data_received, pr.tariff_data_received
            FROM lifecycle.process_request pr
            JOIN portfolio.contract c ON c.gsrn = pr.gsrn
            WHERE c.customer_id = @CustomerId
            ORDER BY pr.created_at DESC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProcessRequest>(
            new CommandDefinition(sql, new { CustomerId = customerId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<ProcessRequest?> GetCompletedByGsrnAsync(string gsrn, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_type, gsrn, status, effective_date, datahub_correlation_id, customer_data_received, tariff_data_received
            FROM lifecycle.process_request
            WHERE gsrn = @Gsrn AND status = 'completed'
            ORDER BY created_at DESC LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProcessRequest>(
            new CommandDefinition(sql, new { Gsrn = gsrn }, cancellationToken: ct));
    }

    public async Task<PagedResult<ProcessListItem>> GetProcessesPagedAsync(
        string? status, string? processType, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        var conditions = new List<string>();
        if (!string.IsNullOrEmpty(status))
            conditions.Add("pr.status = @Status");
        if (!string.IsNullOrEmpty(processType))
            conditions.Add("pr.process_type = @ProcessType");
        if (!string.IsNullOrEmpty(search))
            conditions.Add("pr.gsrn LIKE @Search || '%'");

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        var countSql = $"SELECT COUNT(*) FROM lifecycle.process_request pr {whereClause}";

        var dataSql = $"""
            SELECT pr.id, pr.process_type, pr.gsrn, pr.status, pr.effective_date,
                   pr.datahub_correlation_id, pr.created_at,
                   c.name AS customer_name, ct.customer_id
            FROM lifecycle.process_request pr
            LEFT JOIN portfolio.contract ct ON ct.gsrn = pr.gsrn AND ct.end_date IS NULL
            LEFT JOIN portfolio.customer c ON c.id = ct.customer_id
            {whereClause}
            ORDER BY pr.created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var parameters = new
        {
            Status = status,
            ProcessType = processType,
            Search = search,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        };

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var totalCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: ct));
        var items = await conn.QueryAsync<ProcessListItem>(
            new CommandDefinition(dataSql, parameters, cancellationToken: ct));

        return new PagedResult<ProcessListItem>(items.ToList(), totalCount, page, pageSize);
    }

    private sealed class ProcessDetailRow
    {
        public Guid Id { get; set; }
        public string ProcessType { get; set; } = null!;
        public string Gsrn { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateOnly? EffectiveDate { get; set; }
        public string? DatahubCorrelationId { get; set; }
        public bool CustomerDataReceived { get; set; }
        public bool TariffDataReceived { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class ReceivedMessageRow
    {
        public string MessageType { get; set; } = null!;
        public string? Status { get; set; }
        public DateTime? ReceivedAt { get; set; }
    }
}
