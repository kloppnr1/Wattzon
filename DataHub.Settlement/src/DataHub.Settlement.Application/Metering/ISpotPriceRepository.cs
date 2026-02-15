namespace DataHub.Settlement.Application.Metering;

public interface ISpotPriceRepository
{
    Task StorePricesAsync(IReadOnlyList<SpotPriceRow> prices, CancellationToken ct);
    Task<decimal> GetPriceAsync(string priceArea, DateTime hour, CancellationToken ct);
    Task<IReadOnlyList<SpotPriceRow>> GetPricesAsync(string priceArea, DateTime from, DateTime to, CancellationToken ct);
    Task<SpotPricePagedResult> GetPricesPagedAsync(string priceArea, DateTime from, DateTime to, int page, int pageSize, CancellationToken ct);
    Task<DateOnly?> GetLatestPriceDateAsync(string priceArea, CancellationToken ct);
    Task<DateOnly?> GetEarliestPriceDateAsync(string priceArea, CancellationToken ct);
    Task<SpotPriceAreaStatus> GetAreaStatusAsync(string priceArea, CancellationToken ct);
}
