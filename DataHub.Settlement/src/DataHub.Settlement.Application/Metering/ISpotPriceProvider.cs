namespace DataHub.Settlement.Application.Metering;

/// <summary>
/// Fetches spot prices from an external market data source (e.g. Energi Data Service / Nord Pool).
/// </summary>
public interface ISpotPriceProvider
{
    /// <summary>
    /// Fetch day-ahead spot prices for a price area and date range.
    /// Returns prices in øre/kWh at the native resolution of the source
    /// (PT1H for dates before Oct 2025, PT15M from Oct 2025 onward).
    /// </summary>
    Task<IReadOnlyList<SpotPriceRow>> FetchPricesAsync(
        string priceArea, DateOnly from, DateOnly to, CancellationToken ct);
}
