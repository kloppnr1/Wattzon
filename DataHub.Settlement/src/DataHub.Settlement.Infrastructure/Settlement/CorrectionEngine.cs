using DataHub.Settlement.Application.Settlement;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class CorrectionEngine
{
    private const decimal VatRate = 0.25m;

    public CorrectionResult Calculate(CorrectionRequest request)
    {
        var spotPriceByHour = request.SpotPrices.ToDictionary(p => p.Hour, p => p.PricePerKwh / 100m);
        var gridRateByHour = request.GridTariffRates.ToDictionary(r => r.HourNumber, r => r.PricePerKwh);

        decimal totalDeltaKwh = 0m;
        decimal energyDelta = 0m;
        decimal gridTariffDelta = 0m;

        foreach (var delta in request.Deltas)
        {
            var dKwh = delta.DeltaKwh;
            totalDeltaKwh += dKwh;

            if (spotPriceByHour.TryGetValue(delta.Timestamp, out var spotPrice))
            {
                energyDelta += dKwh * (spotPrice + request.MarginPerKwh + request.SupplementPerKwh);
            }

            var hourNumber = delta.Timestamp.Hour + 1;
            if (gridRateByHour.TryGetValue(hourNumber, out var gridRate))
            {
                gridTariffDelta += dKwh * gridRate;
            }
        }

        var systemDelta = totalDeltaKwh * request.SystemTariffRate;
        var transmissionDelta = totalDeltaKwh * request.TransmissionTariffRate;
        var electricityTaxDelta = totalDeltaKwh * request.ElectricityTaxRate;

        // No subscription adjustments for corrections
        var lines = new List<SettlementLine>
        {
            new("energy", totalDeltaKwh, Math.Round(energyDelta, 2)),
            new("grid_tariff", totalDeltaKwh, Math.Round(gridTariffDelta, 2)),
            new("system_tariff", totalDeltaKwh, Math.Round(systemDelta, 2)),
            new("transmission_tariff", totalDeltaKwh, Math.Round(transmissionDelta, 2)),
            new("electricity_tax", totalDeltaKwh, Math.Round(electricityTaxDelta, 2)),
        };

        var subtotal = lines.Sum(l => l.Amount);
        var vatAmount = Math.Round(subtotal * VatRate, 2);
        var total = subtotal + vatAmount;

        return new CorrectionResult(
            request.MeteringPointId, request.PeriodStart, request.PeriodEnd,
            totalDeltaKwh, lines, subtotal, vatAmount, total);
    }
}
