namespace DataHub.Settlement.Application.Messaging;

public record MessageFilter(
    string? MessageType,
    string? Status,
    string? CorrelationId,
    DateTime? FromDate,
    DateTime? ToDate,
    string? QueueName);

public record OutboundFilter(
    string? ProcessType,
    string? Status,
    string? CorrelationId,
    DateTime? FromDate,
    DateTime? ToDate);

public record InboundMessageSummary(
    Guid Id,
    string DatahubMessageId,
    string MessageType,
    string? CorrelationId,
    string QueueName,
    string Status,
    DateTime ReceivedAt,
    DateTime? ProcessedAt);

public record InboundMessageDetail(
    Guid Id,
    string DatahubMessageId,
    string MessageType,
    string? CorrelationId,
    string QueueName,
    string Status,
    DateTime ReceivedAt,
    DateTime? ProcessedAt,
    string? ErrorDetails,
    int RawPayloadSize);

public record OutboundRequestSummary(
    Guid Id,
    string ProcessType,
    string Gsrn,
    string Status,
    string? CorrelationId,
    DateTime SentAt,
    DateTime? ResponseAt);

public record OutboundRequestDetail(
    Guid Id,
    string ProcessType,
    string Gsrn,
    string Status,
    string? CorrelationId,
    DateTime SentAt,
    DateTime? ResponseAt,
    string? ErrorDetails);

public record DeadLetterSummary(
    Guid Id,
    string? OriginalMessageId,
    string QueueName,
    string ErrorReason,
    DateTime FailedAt,
    bool Resolved);

public record DeadLetterDetail(
    Guid Id,
    string? OriginalMessageId,
    string QueueName,
    string ErrorReason,
    DateTime FailedAt,
    bool Resolved,
    string RawPayload,
    DateTime? ResolvedAt,
    string? ResolvedBy);

public record MessageStats(
    int TotalInbound,
    int ProcessedCount,
    int DeadLetterCount,
    int PendingOutbound);

public record ConversationSummary(
    Guid ProcessRequestId,
    string Gsrn,
    string ProcessType,
    string ProcessStatus,
    DateOnly? EffectiveDate,
    string CorrelationId,
    DateTime CreatedAt,
    string? CustomerName,
    string? SignupNumber,
    int OutboundCount,
    DateTime? FirstSentAt,
    int InboundCount,
    DateTime? LastReceivedAt,
    bool HasAcknowledgement,
    bool HasActivation);

public record ConversationDetail(
    string CorrelationId,
    IReadOnlyList<OutboundRequestSummary> Outbound,
    IReadOnlyList<InboundMessageSummary> Inbound);

public record DataDeliverySummary(
    DateTime DeliveryDate,
    string MessageType,
    int MessageCount,
    int ProcessedCount,
    int ErrorCount);
