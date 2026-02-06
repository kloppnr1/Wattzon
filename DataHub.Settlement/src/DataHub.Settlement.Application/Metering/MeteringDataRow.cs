namespace DataHub.Settlement.Application.Metering;

public record MeteringDataRow(
    DateTime Timestamp,
    string Resolution,
    decimal QuantityKwh,
    string QualityCode,
    string SourceMessageId);
