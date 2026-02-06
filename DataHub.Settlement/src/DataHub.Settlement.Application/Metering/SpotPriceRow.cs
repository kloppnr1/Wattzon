namespace DataHub.Settlement.Application.Metering;

public record SpotPriceRow(string PriceArea, DateTime Hour, decimal PricePerKwh);
