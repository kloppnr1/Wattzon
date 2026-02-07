namespace DataHub.Settlement.Application.Metering;

public interface IMeteringDataRepository
{
    Task StoreTimeSeriesAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct);
    Task<int> StoreTimeSeriesWithHistoryAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct);
    Task<IReadOnlyList<MeteringDataRow>> GetConsumptionAsync(string meteringPointId, DateTime from, DateTime to, CancellationToken ct);
    Task<IReadOnlyList<MeteringDataChange>> GetChangesAsync(string meteringPointId, DateTime from, DateTime to, CancellationToken ct);
}

public record MeteringDataChange(
    string MeteringPointId,
    DateTime Timestamp,
    decimal PreviousKwh,
    decimal NewKwh,
    string? PreviousMessageId,
    string? NewMessageId,
    DateTime ChangedAt);
