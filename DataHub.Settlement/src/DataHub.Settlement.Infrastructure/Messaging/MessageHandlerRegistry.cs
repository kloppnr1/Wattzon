using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Messaging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class MessageHandlerRegistry
{
    private readonly Dictionary<QueueName, IMessageHandler> _handlers = new();
    private readonly Dictionary<QueueName, Func<DataHubMessage, CancellationToken, Task>> _reprocessors = new();

    public void Register<THandler>(THandler handler, QueuePoller<THandler> poller)
        where THandler : IMessageHandler
    {
        _handlers[handler.Queue] = handler;
        _reprocessors[handler.Queue] = poller.ReprocessMessageAsync;
    }

    public Task ReprocessMessageAsync(DataHubMessage message, QueueName queue, CancellationToken ct)
    {
        if (!_reprocessors.TryGetValue(queue, out var reprocess))
            throw new InvalidOperationException($"No handler registered for queue {queue}");

        return reprocess(message, ct);
    }
}
