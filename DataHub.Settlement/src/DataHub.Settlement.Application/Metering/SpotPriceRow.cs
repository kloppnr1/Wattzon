namespace DataHub.Settlement.Application.Metering;

/// <summary>
/// A single spot price data point. PricePerKwh is stored in øre/kWh.
/// Resolution is PT1H (hourly, pre-Oct 2025) or PT15M (quarter-hourly, post-Oct 2025).
/// </summary>
public record SpotPriceRow(string PriceArea, DateTime Timestamp, decimal PricePerKwh, string Resolution = "PT1H");

public record SpotPricePagedResult(
    IReadOnlyList<SpotPriceRow> Items,
    int TotalCount,
    decimal AvgPrice,
    decimal MinPrice,
    decimal MaxPrice);
