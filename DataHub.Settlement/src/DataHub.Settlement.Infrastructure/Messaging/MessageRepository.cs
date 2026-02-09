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

    public async Task<PagedResult<ConversationSummary>> GetConversationsAsync(int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        const string sql = """
            SELECT pr.id AS process_request_id, pr.gsrn, pr.process_type, pr.status AS process_status,
                   pr.effective_date, pr.datahub_correlation_id AS correlation_id,
                   pr.created_at,
                   s.customer_name, s.signup_number,
                   (SELECT COUNT(*) FROM datahub.outbound_request o
                    WHERE o.correlation_id = pr.datahub_correlation_id) AS outbound_count,
                   (SELECT MIN(o.sent_at) FROM datahub.outbound_request o
                    WHERE o.correlation_id = pr.datahub_correlation_id) AS first_sent_at,
                   (SELECT COUNT(*) FROM datahub.inbound_message i
                    WHERE i.correlation_id = pr.datahub_correlation_id) AS inbound_count,
                   (SELECT MAX(i.received_at) FROM datahub.inbound_message i
                    WHERE i.correlation_id = pr.datahub_correlation_id) AS last_received_at,
                   EXISTS(SELECT 1 FROM datahub.inbound_message i
                    WHERE i.correlation_id = pr.datahub_correlation_id AND i.message_type = 'RSM-009') AS has_acknowledgement,
                   EXISTS(SELECT 1 FROM datahub.inbound_message i
                    WHERE i.correlation_id = pr.datahub_correlation_id AND i.message_type = 'RSM-007') AS has_activation,
                   COUNT(*) OVER() AS total_count
            FROM lifecycle.process_request pr
            LEFT JOIN portfolio.signup s ON s.process_request_id = pr.id
            WHERE pr.datahub_correlation_id IS NOT NULL
            ORDER BY pr.created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<ConversationSummaryRow>(sql, new { PageSize = pageSize, Offset = offset });
        var rowList = rows.ToList();

        var totalCount = rowList.FirstOrDefault()?.TotalCount ?? 0;
        var items = rowList.Select(r => new ConversationSummary(
            r.ProcessRequestId,
            r.Gsrn,
            r.ProcessType,
            r.ProcessStatus,
            r.EffectiveDate,
            r.CorrelationId,
            r.CreatedAt,
            r.CustomerName,
            r.SignupNumber,
            r.OutboundCount,
            r.FirstSentAt,
            r.InboundCount,
            r.LastReceivedAt,
            r.HasAcknowledgement,
            r.HasActivation)).ToList();

        return new PagedResult<ConversationSummary>(items, totalCount, page, pageSize);
    }

    public async Task<ConversationDetail?> GetConversationAsync(string correlationId, CancellationToken ct)
    {
        const string outboundSql = """
            SELECT id, process_type, gsrn, status, correlation_id, sent_at, response_at
            FROM datahub.outbound_request
            WHERE correlation_id = @CorrelationId
            ORDER BY sent_at ASC
            """;

        const string inboundSql = """
            SELECT id, datahub_message_id, message_type, correlation_id, queue_name, status, received_at, processed_at
            FROM datahub.inbound_message
            WHERE correlation_id = @CorrelationId
            ORDER BY received_at ASC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var outboundRows = await conn.QueryAsync<OutboundRequestSummaryRow>(outboundSql, new { CorrelationId = correlationId });
        var inboundRows = await conn.QueryAsync<InboundMessageSummaryRow>(inboundSql, new { CorrelationId = correlationId });

        var outbound = outboundRows.Select(r => new OutboundRequestSummary(r.Id, r.ProcessType, r.Gsrn, r.Status, r.CorrelationId, r.SentAt, r.ResponseAt)).ToList();
        var inbound = inboundRows.Select(r => new InboundMessageSummary(r.Id, r.DatahubMessageId, r.MessageType, r.CorrelationId, r.QueueName, r.Status, r.ReceivedAt, r.ProcessedAt)).ToList();

        if (outbound.Count == 0 && inbound.Count == 0)
            return null;

        return new ConversationDetail(correlationId, outbound, inbound);
    }

    public async Task<IReadOnlyList<DataDeliverySummary>> GetDataDeliveriesAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT DATE(received_at) AS delivery_date, message_type,
                   COUNT(*) AS message_count,
                   SUM(CASE WHEN status = 'processed' THEN 1 ELSE 0 END) AS processed_count,
                   SUM(CASE WHEN status = 'dead_lettered' THEN 1 ELSE 0 END) AS error_count
            FROM datahub.inbound_message
            WHERE message_type IN ('RSM-012', 'RSM-014', 'RSM-004')
            GROUP BY DATE(received_at), message_type
            ORDER BY delivery_date DESC, message_type
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<DataDeliverySummaryRow>(sql);

        return rows.Select(r => new DataDeliverySummary(
            r.DeliveryDate,
            r.MessageType,
            r.MessageCount,
            r.ProcessedCount,
            r.ErrorCount)).ToList();
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

internal class ConversationSummaryRow
{
    public Guid ProcessRequestId { get; set; }
    public string Gsrn { get; set; } = null!;
    public string ProcessType { get; set; } = null!;
    public string ProcessStatus { get; set; } = null!;
    public DateOnly? EffectiveDate { get; set; }
    public string CorrelationId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string? CustomerName { get; set; }
    public string? SignupNumber { get; set; }
    public int OutboundCount { get; set; }
    public DateTime? FirstSentAt { get; set; }
    public int InboundCount { get; set; }
    public DateTime? LastReceivedAt { get; set; }
    public bool HasAcknowledgement { get; set; }
    public bool HasActivation { get; set; }
    public int TotalCount { get; set; }
}

internal class DataDeliverySummaryRow
{
    public DateTime DeliveryDate { get; set; }
    public string MessageType { get; set; } = null!;
    public int MessageCount { get; set; }
    public int ProcessedCount { get; set; }
    public int ErrorCount { get; set; }
}
