using Dapper;
using DataHub.Settlement.Application.Common;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class MessageRepository : IMessageRepository
{
    private readonly string _connectionString;

    static MessageRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public MessageRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<PagedResult<InboundMessageSummary>> GetInboundMessagesAsync(MessageFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        var sql = """
            SELECT im.id, im.datahub_message_id, im.message_type, im.correlation_id, im.queue_name, im.status, im.received_at, im.processed_at,
                   COUNT(*) OVER() AS total_count
            FROM datahub.inbound_message im
            WHERE 1=1
            """;

        if (!string.IsNullOrWhiteSpace(filter.MessageType))
            sql += " AND im.message_type = @MessageType\n";
        if (!string.IsNullOrWhiteSpace(filter.Status))
            sql += " AND im.status = @Status\n";
        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
            sql += " AND im.correlation_id = @CorrelationId\n";
        if (!string.IsNullOrWhiteSpace(filter.QueueName))
            sql += " AND im.queue_name = @QueueName\n";
        if (filter.FromDate.HasValue)
            sql += " AND im.received_at >= @FromDate\n";
        if (filter.ToDate.HasValue)
            sql += " AND im.received_at <= @ToDate\n";

        sql += " ORDER BY im.received_at DESC LIMIT @PageSize OFFSET @Offset";

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<InboundMessageSummaryRow>(sql, new
        {
            filter.MessageType,
            filter.Status,
            filter.CorrelationId,
            filter.QueueName,
            filter.FromDate,
            filter.ToDate,
            PageSize = pageSize,
            Offset = offset
        });
        var rowList = rows.ToList();

        var totalCount = rowList.FirstOrDefault()?.TotalCount ?? 0;
        var items = rowList.Select(r => new InboundMessageSummary(
            r.Id,
            r.DatahubMessageId,
            r.MessageType,
            r.CorrelationId,
            r.QueueName,
            r.Status,
            r.ReceivedAt,
            r.ProcessedAt)).ToList();

        return new PagedResult<InboundMessageSummary>(items, totalCount, page, pageSize);
    }

    public async Task<InboundMessageDetail?> GetInboundMessageAsync(Guid messageId, CancellationToken ct)
    {
        const string sql = """
            SELECT im.id, im.datahub_message_id, im.message_type, im.correlation_id, im.queue_name, im.status, im.received_at, im.processed_at,
                   COALESCE(im.raw_payload_size, 0) AS raw_payload_size,
                   dl.error_reason AS error_details
            FROM datahub.inbound_message im
            LEFT JOIN datahub.dead_letter dl ON im.id::text = dl.original_message_id
            WHERE im.id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QuerySingleOrDefaultAsync<InboundMessageDetailRow>(sql, new { Id = messageId });
        if (row is null)
            return null;

        return new InboundMessageDetail(
            row.Id,
            row.DatahubMessageId,
            row.MessageType,
            row.CorrelationId,
            row.QueueName,
            row.Status,
            row.ReceivedAt,
            row.ProcessedAt,
            row.ErrorDetails,
            row.RawPayloadSize);
    }

    public async Task<PagedResult<OutboundRequestSummary>> GetOutboundRequestsAsync(OutboundFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        var sql = """
            SELECT orq.id, orq.process_type, orq.gsrn, orq.status, orq.correlation_id, orq.sent_at, orq.response_at,
                   COUNT(*) OVER() AS total_count
            FROM datahub.outbound_request orq
            WHERE 1=1
            """;

        if (!string.IsNullOrWhiteSpace(filter.ProcessType))
            sql += " AND orq.process_type = @ProcessType\n";
        if (!string.IsNullOrWhiteSpace(filter.Status))
            sql += " AND orq.status = @Status\n";
        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
            sql += " AND orq.correlation_id = @CorrelationId\n";
        if (filter.FromDate.HasValue)
            sql += " AND orq.sent_at >= @FromDate\n";
        if (filter.ToDate.HasValue)
            sql += " AND orq.sent_at <= @ToDate\n";

        sql += " ORDER BY orq.sent_at DESC LIMIT @PageSize OFFSET @Offset";

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<OutboundRequestSummaryRow>(sql, new
        {
            filter.ProcessType,
            filter.Status,
            filter.CorrelationId,
            filter.FromDate,
            filter.ToDate,
            PageSize = pageSize,
            Offset = offset
        });
        var rowList = rows.ToList();

        var totalCount = rowList.FirstOrDefault()?.TotalCount ?? 0;
        var items = rowList.Select(r => new OutboundRequestSummary(
            r.Id,
            r.ProcessType,
            r.Gsrn,
            r.Status,
            r.CorrelationId,
            r.SentAt,
            r.ResponseAt)).ToList();

        return new PagedResult<OutboundRequestSummary>(items, totalCount, page, pageSize);
    }

    public async Task<OutboundRequestDetail?> GetOutboundRequestAsync(Guid requestId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, process_type, gsrn, status, correlation_id, sent_at, response_at, error_details
            FROM datahub.outbound_request
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QuerySingleOrDefaultAsync<OutboundRequestDetailRow>(sql, new { Id = requestId });
        if (row is null)
            return null;

        return new OutboundRequestDetail(
            row.Id,
            row.ProcessType,
            row.Gsrn,
            row.Status,
            row.CorrelationId,
            row.SentAt,
            row.ResponseAt,
            row.ErrorDetails);
    }

    public async Task<PagedResult<DeadLetterSummary>> GetDeadLettersAsync(bool? resolvedOnly, int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        var sql = """
            SELECT dl.id, dl.original_message_id, dl.queue_name, dl.error_reason, dl.failed_at, dl.resolved,
                   COUNT(*) OVER() AS total_count
            FROM datahub.dead_letter dl
            WHERE 1=1
            """;

        if (resolvedOnly.HasValue)
            sql += " AND dl.resolved = @Resolved\n";

        sql += " ORDER BY dl.failed_at DESC LIMIT @PageSize OFFSET @Offset";

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<DeadLetterSummaryRow>(sql, new
        {
            Resolved = resolvedOnly,
            PageSize = pageSize,
            Offset = offset
        });
        var rowList = rows.ToList();

        var totalCount = rowList.FirstOrDefault()?.TotalCount ?? 0;
        var items = rowList.Select(r => new DeadLetterSummary(
            r.Id,
            r.OriginalMessageId,
            r.QueueName,
            r.ErrorReason,
            r.FailedAt,
            r.Resolved)).ToList();

        return new PagedResult<DeadLetterSummary>(items, totalCount, page, pageSize);
    }

    public async Task<DeadLetterDetail?> GetDeadLetterAsync(Guid deadLetterId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, original_message_id, queue_name, error_reason, failed_at, resolved, raw_payload::text AS raw_payload, resolved_at, resolved_by
            FROM datahub.dead_letter
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QuerySingleOrDefaultAsync<DeadLetterDetailRow>(sql, new { Id = deadLetterId });
        if (row is null)
            return null;

        return new DeadLetterDetail(
            row.Id,
            row.OriginalMessageId,
            row.QueueName,
            row.ErrorReason,
            row.FailedAt,
            row.Resolved,
            row.RawPayload,
            row.ResolvedAt,
            row.ResolvedBy);
    }

    public async Task<MessageStats> GetMessageStatsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM datahub.inbound_message) AS total_inbound,
                (SELECT COUNT(*) FROM datahub.inbound_message WHERE status = 'processed') AS processed_count,
                (SELECT COUNT(*) FROM datahub.dead_letter WHERE resolved = false) AS dead_letter_count,
                (SELECT COUNT(*) FROM datahub.outbound_request WHERE status = 'sent') AS pending_outbound
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QuerySingleAsync<MessageStatsRow>(sql);

        return new MessageStats(
            row.TotalInbound,
            row.ProcessedCount,
            row.DeadLetterCount,
            row.PendingOutbound);
    }

}

// DTOs for Dapper mapping
internal class InboundMessageSummaryRow
{
    public Guid Id { get; set; }
    public string DatahubMessageId { get; set; } = null!;
    public string MessageType { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public string QueueName { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int TotalCount { get; set; }
}

internal class InboundMessageDetailRow
{
    public Guid Id { get; set; }
    public string DatahubMessageId { get; set; } = null!;
    public string MessageType { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public string QueueName { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RawPayloadSize { get; set; }
    public string? ErrorDetails { get; set; }
}

internal class OutboundRequestSummaryRow
{
    public Guid Id { get; set; }
    public string ProcessType { get; set; } = null!;
    public string Gsrn { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? ResponseAt { get; set; }
    public int TotalCount { get; set; }
}

internal class OutboundRequestDetailRow
{
    public Guid Id { get; set; }
    public string ProcessType { get; set; } = null!;
    public string Gsrn { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? ResponseAt { get; set; }
    public string? ErrorDetails { get; set; }
}

internal class DeadLetterSummaryRow
{
    public Guid Id { get; set; }
    public string? OriginalMessageId { get; set; }
    public string QueueName { get; set; } = null!;
    public string ErrorReason { get; set; } = null!;
    public DateTime FailedAt { get; set; }
    public bool Resolved { get; set; }
    public int TotalCount { get; set; }
}

internal class DeadLetterDetailRow
{
    public Guid Id { get; set; }
    public string? OriginalMessageId { get; set; }
    public string QueueName { get; set; } = null!;
    public string ErrorReason { get; set; } = null!;
    public DateTime FailedAt { get; set; }
    public bool Resolved { get; set; }
    public string RawPayload { get; set; } = null!;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
}

internal class MessageStatsRow
{
    public int TotalInbound { get; set; }
    public int ProcessedCount { get; set; }
    public int DeadLetterCount { get; set; }
    public int PendingOutbound { get; set; }
}
