using System.Collections.Concurrent;

namespace DataHub.Settlement.Simulator;

public sealed class SimulatorState
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueueMessage>> _queues = new();
    private readonly ConcurrentBag<OutboundRequest> _requests = new();
    private readonly ConcurrentDictionary<string, string> _activeGsrns = new();
    private readonly ConcurrentBag<PendingEffectuation> _pendingEffectuations = new();
    private int _messageCounter;

    public bool IsGsrnActive(string gsrn) => _activeGsrns.ContainsKey(gsrn);
    public void ActivateGsrn(string gsrn) => _activeGsrns[gsrn] = "active";
    public void DeactivateGsrn(string gsrn) => _activeGsrns.TryRemove(gsrn, out _);

    public string EnqueueMessage(string queue, string messageType, string? correlationId, string payload)
    {
        var q = _queues.GetOrAdd(queue, _ => new ConcurrentQueue<QueueMessage>());
        var messageId = $"sim-msg-{Interlocked.Increment(ref _messageCounter):D6}";
        q.Enqueue(new QueueMessage(messageId, messageType, correlationId, payload));
        return messageId;
    }

    public QueueMessage? Peek(string queue)
    {
        if (_queues.TryGetValue(queue, out var q) && q.TryPeek(out var msg))
            return msg;
        return null;
    }

    public bool Dequeue(string messageId)
    {
        foreach (var kvp in _queues)
        {
            var original = kvp.Value;
            var replacement = new ConcurrentQueue<QueueMessage>();
            var found = false;

            foreach (var msg in original)
            {
                if (msg.MessageId == messageId)
                    found = true;
                else
                    replacement.Enqueue(msg);
            }

            if (found)
            {
                _queues[kvp.Key] = replacement;
                return true;
            }
        }

        return false;
    }

    public void RecordRequest(string processType, string endpoint, string payload)
    {
        _requests.Add(new OutboundRequest(processType, endpoint, payload, DateTime.UtcNow));
    }

    public IReadOnlyList<OutboundRequest> GetRequests() => _requests.ToList();

    public void ScheduleEffectuation(string gsrn, string correlationId, DateOnly effectiveDate)
    {
        _pendingEffectuations.Add(new PendingEffectuation(gsrn, correlationId, effectiveDate, Enqueued: false));
    }

    public void FlushReadyEffectuations()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var pe in _pendingEffectuations)
        {
            if (pe.Enqueued || pe.EffectiveDate > today) continue;
            pe.Enqueued = true;
            EnqueueMessage("MasterData", "RSM-007", pe.CorrelationId,
                ScenarioLoader.BuildRsm007Json(pe.Gsrn, pe.EffectiveDate.ToString("yyyy-MM-dd") + "T00:00:00Z"));
        }
    }

    public IReadOnlyList<PendingEffectuation> GetPendingEffectuations() => _pendingEffectuations.ToList();

    public void Reset()
    {
        _queues.Clear();
        _requests.Clear();
        _activeGsrns.Clear();
        _pendingEffectuations.Clear();
        Interlocked.Exchange(ref _messageCounter, 0);
    }
}

public record QueueMessage(string MessageId, string MessageType, string? CorrelationId, string Payload);

public record OutboundRequest(string ProcessType, string Endpoint, string Payload, DateTime ReceivedAt);

public class PendingEffectuation(string Gsrn, string CorrelationId, DateOnly EffectiveDate, bool Enqueued)
{
    public string Gsrn { get; } = Gsrn;
    public string CorrelationId { get; } = CorrelationId;
    public DateOnly EffectiveDate { get; } = EffectiveDate;
    public bool Enqueued { get; set; } = Enqueued;
}
