using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Tariff;

namespace DataHub.Settlement.Application.Settlement;

public record SettlementInput(
    string MeteringPointId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<MeteringDataRow> Consumption,
    IReadOnlyList<SpotPriceRow> SpotPrices,
    IReadOnlyList<TariffRateRow> GridTariffRates,
    decimal SystemTariffRate,
    decimal TransmissionTariffRate,
    decimal ElectricityTaxRate,
    decimal GridSubscriptionPerMonth,
    decimal MarginPerKwh,
    decimal SupplementPerKwh,
    decimal SupplierSubscriptionPerMonth,
    ElvarmeOptions? Elvarme = null);

public interface ISettlementDataLoader
{
    Task<SettlementInput> LoadAsync(string gsrn, string gridAreaCode, string priceArea,
        DateOnly periodStart, DateOnly periodEnd,
        decimal marginPerKwh, decimal supplementPerKwh, decimal supplierSubscriptionPerMonth,
        CancellationToken ct);
}
