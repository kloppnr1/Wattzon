using DataHub.Settlement.Application.Common;

namespace DataHub.Settlement.Application.Messaging;

public interface IMessageRepository
{
    Task<PagedResult<InboundMessageSummary>> GetInboundMessagesAsync(MessageFilter filter, int page, int pageSize, CancellationToken ct);
    Task<InboundMessageDetail?> GetInboundMessageAsync(Guid messageId, CancellationToken ct);
    Task<PagedResult<OutboundRequestSummary>> GetOutboundRequestsAsync(OutboundFilter filter, int page, int pageSize, CancellationToken ct);
    Task<OutboundRequestDetail?> GetOutboundRequestAsync(Guid requestId, CancellationToken ct);
    Task<PagedResult<DeadLetterSummary>> GetDeadLettersAsync(bool? resolvedOnly, int page, int pageSize, CancellationToken ct);
    Task<DeadLetterDetail?> GetDeadLetterAsync(Guid deadLetterId, CancellationToken ct);
    Task<MessageStats> GetMessageStatsAsync(CancellationToken ct);
    Task<PagedResult<ConversationSummary>> GetConversationsAsync(int page, int pageSize, CancellationToken ct);
    Task<ConversationDetail?> GetConversationAsync(string correlationId, CancellationToken ct);
    Task<IReadOnlyList<DataDeliverySummary>> GetDataDeliveriesAsync(CancellationToken ct);
}
