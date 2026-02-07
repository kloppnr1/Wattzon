using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Tariff;

namespace DataHub.Settlement.Application.Settlement;

public sealed class PeriodSplitter
{
    private readonly ISettlementEngine _engine;

    public PeriodSplitter(ISettlementEngine engine)
    {
        _engine = engine;
    }

    public SettlementResult CalculateWithTariffChange(
        SettlementRequest request,
        DateOnly tariffChangeDate,
        IReadOnlyList<TariffRateRow> newGridTariffRates,
        decimal? newSystemTariffRate = null,
        decimal? newTransmissionTariffRate = null,
        decimal? newElectricityTaxRate = null)
    {
        var changeTimestamp = tariffChangeDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var beforeConsumption = request.Consumption.Where(r => r.Timestamp < changeTimestamp).ToList();
        var afterConsumption = request.Consumption.Where(r => r.Timestamp >= changeTimestamp).ToList();

        var beforeSpotPrices = request.SpotPrices.Where(p => p.Hour < changeTimestamp).ToList();
        var afterSpotPrices = request.SpotPrices.Where(p => p.Hour >= changeTimestamp).ToList();

        // Before tariff change: use original rates, but period ends at change date
        var beforeRequest = request with
        {
            PeriodEnd = tariffChangeDate,
            Consumption = beforeConsumption,
            SpotPrices = beforeSpotPrices,
        };

        // After tariff change: use new rates, period starts at change date
        var afterRequest = request with
        {
            PeriodStart = tariffChangeDate,
            Consumption = afterConsumption,
            SpotPrices = afterSpotPrices,
            GridTariffRates = newGridTariffRates,
            SystemTariffRate = newSystemTariffRate ?? request.SystemTariffRate,
            TransmissionTariffRate = newTransmissionTariffRate ?? request.TransmissionTariffRate,
            ElectricityTaxRate = newElectricityTaxRate ?? request.ElectricityTaxRate,
        };

        var beforeResult = _engine.Calculate(beforeRequest);
        var afterResult = _engine.Calculate(afterRequest);

        // Combine the results
        var combinedLines = new List<SettlementLine>();
        var chargeTypes = beforeResult.Lines.Select(l => l.ChargeType)
            .Union(afterResult.Lines.Select(l => l.ChargeType))
            .Distinct();

        foreach (var ct in chargeTypes)
        {
            var beforeLine = beforeResult.Lines.FirstOrDefault(l => l.ChargeType == ct);
            var afterLine = afterResult.Lines.FirstOrDefault(l => l.ChargeType == ct);
            var combinedKwh = (beforeLine?.Kwh ?? 0) + (afterLine?.Kwh ?? 0);
            var combinedAmount = (beforeLine?.Amount ?? 0) + (afterLine?.Amount ?? 0);
            combinedLines.Add(new SettlementLine(ct, combinedKwh > 0 ? combinedKwh : null, combinedAmount));
        }

        var subtotal = combinedLines.Sum(l => l.Amount);
        var vatAmount = Math.Round(subtotal * 0.25m, 2);
        var total = subtotal + vatAmount;

        return new SettlementResult(
            request.MeteringPointId,
            request.PeriodStart,
            request.PeriodEnd,
            beforeResult.TotalKwh + afterResult.TotalKwh,
            combinedLines,
            subtotal,
            vatAmount,
            total);
    }
}
