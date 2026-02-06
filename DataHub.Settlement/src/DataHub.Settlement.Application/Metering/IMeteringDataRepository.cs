namespace DataHub.Settlement.Application.Metering;

public interface IMeteringDataRepository
{
    Task StoreTimeSeriesAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct);
    Task<IReadOnlyList<MeteringDataRow>> GetConsumptionAsync(string meteringPointId, DateTime from, DateTime to, CancellationToken ct);
}
