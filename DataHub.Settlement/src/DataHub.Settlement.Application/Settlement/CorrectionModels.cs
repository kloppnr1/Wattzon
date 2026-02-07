using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Tariff;

namespace DataHub.Settlement.Application.Settlement;

public record CorrectionRequest(
    string MeteringPointId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<ConsumptionDelta> Deltas,
    IReadOnlyList<SpotPriceRow> SpotPrices,
    IReadOnlyList<TariffRateRow> GridTariffRates,
    decimal SystemTariffRate,
    decimal TransmissionTariffRate,
    decimal ElectricityTaxRate,
    decimal MarginPerKwh,
    decimal SupplementPerKwh);

public record ConsumptionDelta(DateTime Timestamp, decimal OldKwh, decimal NewKwh)
{
    public decimal DeltaKwh => NewKwh - OldKwh;
}

public record CorrectionResult(
    string MeteringPointId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalDeltaKwh,
    IReadOnlyList<SettlementLine> Lines,
    decimal Subtotal,
    decimal VatAmount,
    decimal Total);
