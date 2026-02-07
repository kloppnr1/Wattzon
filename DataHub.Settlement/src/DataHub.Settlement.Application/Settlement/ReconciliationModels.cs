namespace DataHub.Settlement.Application.Settlement;

public record Rsm014Aggregation(
    string GridAreaCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalKwh,
    IReadOnlyList<AggregationPoint> Points);

public record AggregationPoint(DateTime Timestamp, decimal QuantityKwh);

public record ReconciliationResult(
    string GridAreaCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal OwnTotalKwh,
    decimal DataHubTotalKwh,
    decimal DiscrepancyKwh,
    bool IsReconciled,
    IReadOnlyList<ReconciliationDiscrepancy> Discrepancies);

public record ReconciliationDiscrepancy(
    DateTime Timestamp,
    decimal OwnKwh,
    decimal DataHubKwh,
    decimal DeltaKwh);
