using DataHub.Settlement.Application.DataHub;

namespace DataHub.Settlement.Application.Messaging;

public interface IMessageHandler
{
    QueueName Queue { get; }
    Task HandleAsync(DataHubMessage message, CancellationToken ct);
}
