using System.Collections.Concurrent;
using DataHub.Settlement.Application.DataHub;

namespace DataHub.Settlement.UnitTests;

public sealed class FakeDataHubClient : IDataHubClient
{
    private readonly ConcurrentDictionary<QueueName, ConcurrentQueue<DataHubMessage>> _queues = new();

    public void Enqueue(QueueName queue, DataHubMessage message)
    {
        var q = _queues.GetOrAdd(queue, _ => new ConcurrentQueue<DataHubMessage>());
        q.Enqueue(message);
    }

    public Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct)
    {
        if (_queues.TryGetValue(queue, out var q) && q.TryPeek(out var message))
            return Task.FromResult<DataHubMessage?>(message);

        return Task.FromResult<DataHubMessage?>(null);
    }

    public Task DequeueAsync(string messageId, CancellationToken ct)
    {
        foreach (var kvp in _queues)
        {
            var original = kvp.Value;
            var replacement = new ConcurrentQueue<DataHubMessage>();

            foreach (var msg in original)
            {
                if (msg.MessageId != messageId)
                    replacement.Enqueue(msg);
            }

            _queues[kvp.Key] = replacement;
        }

        return Task.CompletedTask;
    }

    public Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct)
    {
        var response = new DataHubResponse(
            CorrelationId: Guid.NewGuid().ToString(),
            Accepted: true,
            RejectionReason: null);

        return Task.FromResult(response);
    }

    public void Reset()
    {
        _queues.Clear();
    }
}
