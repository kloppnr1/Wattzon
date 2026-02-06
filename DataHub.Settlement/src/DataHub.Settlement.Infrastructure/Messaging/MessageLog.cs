using Dapper;
using DataHub.Settlement.Application.Messaging;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class MessageLog : IMessageLog
{
    private readonly string _connectionString;

    public MessageLog(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> IsProcessedAsync(string messageId, CancellationToken ct)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM datahub.processed_message_id WHERE message_id = @MessageId)";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<bool>(
            new CommandDefinition(sql, new { MessageId = messageId }, cancellationToken: ct));
    }

    public async Task MarkProcessedAsync(string messageId, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO datahub.processed_message_id (message_id)
            VALUES (@MessageId)
            ON CONFLICT (message_id) DO NOTHING
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { MessageId = messageId }, cancellationToken: ct));
    }

    public async Task RecordInboundAsync(string messageId, string messageType, string? correlationId,
        string queueName, int payloadSize, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO datahub.inbound_message (datahub_message_id, message_type, correlation_id, queue_name, raw_payload_size)
            VALUES (@MessageId, @MessageType, @CorrelationId, @QueueName, @PayloadSize)
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { MessageId = messageId, MessageType = messageType, CorrelationId = correlationId, QueueName = queueName, PayloadSize = payloadSize },
            cancellationToken: ct));
    }

    public async Task MarkInboundStatusAsync(string messageId, string status, string? errorDetails, CancellationToken ct)
    {
        const string sql = """
            UPDATE datahub.inbound_message
            SET status = @Status, error_details = @ErrorDetails, processed_at = now()
            WHERE datahub_message_id = @MessageId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { MessageId = messageId, Status = status, ErrorDetails = errorDetails }, cancellationToken: ct));
    }

    public async Task DeadLetterAsync(string messageId, string queueName, string errorReason, string rawPayload, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO datahub.dead_letter (original_message_id, queue_name, error_reason, raw_payload)
            VALUES (@MessageId, @QueueName, @ErrorReason, jsonb_build_object('raw', @RawPayload))
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { MessageId = messageId, QueueName = queueName, ErrorReason = errorReason, RawPayload = rawPayload },
            cancellationToken: ct));
    }
}
