using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Tariff;

namespace DataHub.Settlement.Application.Settlement;

public record SettlementRequest(
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
    decimal SupplierSubscriptionPerMonth);

public record SettlementResult(
    string MeteringPointId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalKwh,
    IReadOnlyList<SettlementLine> Lines,
    decimal Subtotal,
    decimal VatAmount,
    decimal Total);

public record SettlementLine(string ChargeType, decimal? Kwh, decimal Amount);
