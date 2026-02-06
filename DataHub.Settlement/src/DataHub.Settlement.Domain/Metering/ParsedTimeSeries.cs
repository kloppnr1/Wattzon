namespace DataHub.Settlement.Domain.Metering;

public record ParsedTimeSeries(
    string TransactionId,
    string MeteringPointId,
    string MeteringPointType,
    string Resolution,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    IReadOnlyList<TimeSeriesPoint> Points);

public record TimeSeriesPoint(
    int Position,
    DateTimeOffset Timestamp,
    decimal QuantityKwh,
    string QualityCode);
