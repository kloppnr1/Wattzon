namespace DataHub.Settlement.Application.Settlement;

public sealed class ReconciliationService
{
    private const decimal ToleranceKwh = 0.001m;

    public ReconciliationResult Reconcile(
        Rsm014Aggregation datahubAggregation,
        IReadOnlyList<AggregationPoint> ownAggregation)
    {
        var ownByTimestamp = ownAggregation.ToDictionary(p => p.Timestamp, p => p.QuantityKwh);
        var discrepancies = new List<ReconciliationDiscrepancy>();

        decimal ownTotal = 0m;
        decimal datahubTotal = datahubAggregation.TotalKwh;

        foreach (var dhPoint in datahubAggregation.Points)
        {
            var ownKwh = ownByTimestamp.GetValueOrDefault(dhPoint.Timestamp, 0m);
            ownTotal += ownKwh;
            var delta = ownKwh - dhPoint.QuantityKwh;

            if (Math.Abs(delta) > ToleranceKwh)
            {
                discrepancies.Add(new ReconciliationDiscrepancy(
                    dhPoint.Timestamp, ownKwh, dhPoint.QuantityKwh, delta));
            }
        }

        // Check for hours we have but DataHub doesn't
        var dhTimestamps = new HashSet<DateTime>(datahubAggregation.Points.Select(p => p.Timestamp));
        foreach (var own in ownAggregation)
        {
            if (!dhTimestamps.Contains(own.Timestamp))
            {
                ownTotal += own.QuantityKwh;
                discrepancies.Add(new ReconciliationDiscrepancy(
                    own.Timestamp, own.QuantityKwh, 0m, own.QuantityKwh));
            }
        }

        var totalDiscrepancy = ownTotal - datahubTotal;

        return new ReconciliationResult(
            datahubAggregation.GridAreaCode,
            datahubAggregation.PeriodStart,
            datahubAggregation.PeriodEnd,
            ownTotal,
            datahubTotal,
            totalDiscrepancy,
            discrepancies.Count == 0,
            discrepancies);
    }
}
