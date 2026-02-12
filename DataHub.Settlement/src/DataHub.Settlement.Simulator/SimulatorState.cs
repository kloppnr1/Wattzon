using System.Collections.Concurrent;

namespace DataHub.Settlement.Simulator;

public sealed class SimulatorState
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueueMessage>> _queues = new();
    private readonly ConcurrentBag<OutboundRequest> _requests = new();
    private readonly ConcurrentDictionary<string, string> _activeGsrns = new();
    private readonly ConcurrentBag<PendingEffectuation> _pendingEffectuations = new();
    private readonly ConcurrentBag<ActiveSupply> _activeSupplies = new();

    public bool IsGsrnActive(string gsrn) => _activeGsrns.ContainsKey(gsrn);
    public void ActivateGsrn(string gsrn) => _activeGsrns[gsrn] = "active";
    public void DeactivateGsrn(string gsrn) => _activeGsrns.TryRemove(gsrn, out _);

    public string EnqueueMessage(string queue, string messageType, string? correlationId, string payload)
    {
        var q = _queues.GetOrAdd(queue, _ => new ConcurrentQueue<QueueMessage>());
        var messageId = $"sim-{Guid.NewGuid():N}";
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
            EnqueueMessage("MasterData", "RSM-022", pe.CorrelationId,
                ScenarioLoader.BuildRsm022Json(pe.Gsrn, pe.EffectiveDate.ToString("yyyy-MM-dd") + "T00:00:00Z"));

            // Enqueue initial RSM-012 covering effective date → today
            var start = new DateTimeOffset(pe.EffectiveDate.Year, pe.EffectiveDate.Month, pe.EffectiveDate.Day, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, TimeSpan.Zero);
            var hours = (int)(end - start).TotalHours;
            if (hours > 0)
            {
                EnqueueMessage("Timeseries", "RSM-012", null,
                    ScenarioLoader.BuildRsm012Json(pe.Gsrn, start, end, hours));
            }

            // For retroactive effectuations, also deliver corrected data for 10 hours on the effective date
            if (pe.EffectiveDate < today)
            {
                EnqueueMessage("Timeseries", "RSM-012", null,
                    ScenarioLoader.BuildCorrectionRsm012Json(pe.Gsrn, pe.EffectiveDate));
            }

            // Track for ongoing daily delivery
            _activeSupplies.Add(new ActiveSupply(pe.Gsrn, pe.EffectiveDate, today));
        }
    }

    public void FlushDailyTimeseries()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var supply in _activeSupplies)
        {
            if (supply.Cancelled || supply.LastDeliveredDate >= today) continue;

            // Deliver one RSM-012 per missing day (yesterday's data, like real DataHub)
            var nextDay = supply.LastDeliveredDate;
            while (nextDay < today)
            {
                var start = new DateTimeOffset(nextDay.Year, nextDay.Month, nextDay.Day, 0, 0, 0, TimeSpan.Zero);
                var end = start.AddHours(24);
                EnqueueMessage("Timeseries", "RSM-012", null,
                    ScenarioLoader.BuildRsm012Json(supply.Gsrn, start, end, 24));
                nextDay = nextDay.AddDays(1);
            }

            supply.LastDeliveredDate = today;
        }
    }

    public void CancelEffectuation(string gsrn)
    {
        foreach (var pe in _pendingEffectuations)
        {
            if (pe.Gsrn == gsrn && !pe.Enqueued)
                pe.Enqueued = true; // Mark as enqueued so FlushReadyEffectuations skips it
        }

        // Also remove from active supplies (supply ended)
        foreach (var supply in _activeSupplies)
        {
            if (supply.Gsrn == gsrn)
                supply.Cancelled = true;
        }
    }

    public IReadOnlyList<PendingEffectuation> GetPendingEffectuations() => _pendingEffectuations.ToList();

    public void Reset()
    {
        _queues.Clear();
        _requests.Clear();
        _activeGsrns.Clear();
        _pendingEffectuations.Clear();
        _activeSupplies.Clear();
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

public class ActiveSupply(string Gsrn, DateOnly EffectiveDate, DateOnly LastDeliveredDate)
{
    public string Gsrn { get; } = Gsrn;
    public DateOnly EffectiveDate { get; } = EffectiveDate;
    public DateOnly LastDeliveredDate { get; set; } = LastDeliveredDate;
    public bool Cancelled { get; set; }
}
