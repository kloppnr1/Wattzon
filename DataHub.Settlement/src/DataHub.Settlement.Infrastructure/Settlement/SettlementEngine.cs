using DataHub.Settlement.Application.Settlement;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class SettlementEngine : ISettlementEngine
{
    private const decimal VatRate = 0.25m;

    public SettlementResult Calculate(SettlementRequest request)
    {
        var spotPriceByHour = request.SpotPrices.ToDictionary(p => p.Hour, p => p.PricePerKwh);
        var gridRateByHour = request.GridTariffRates.ToDictionary(r => r.HourNumber, r => r.PricePerKwh);

        decimal totalKwh = 0m;
        decimal energyTotal = 0m;
        decimal gridTariffTotal = 0m;

        foreach (var row in request.Consumption)
        {
            var kwh = row.QuantityKwh;
            totalKwh += kwh;

            // Energy: kWh × (spot + margin + supplement)
            if (spotPriceByHour.TryGetValue(row.Timestamp, out var spotPrice))
            {
                energyTotal += kwh * (spotPrice + request.MarginPerKwh + request.SupplementPerKwh);
            }

            // Grid tariff: kWh × grid_rate[hour_of_day]
            var hourNumber = row.Timestamp.Hour + 1; // hour 0 → hour_number 1
            if (gridRateByHour.TryGetValue(hourNumber, out var gridRate))
            {
                gridTariffTotal += kwh * gridRate;
            }
        }

        // Flat tariffs
        var systemTotal = totalKwh * request.SystemTariffRate;
        var transmissionTotal = totalKwh * request.TransmissionTariffRate;
        var electricityTaxTotal = totalKwh * request.ElectricityTaxRate;

        // Subscriptions (pro rata for partial periods)
        var periodStart = request.PeriodStart;
        var periodEnd = request.PeriodEnd;
        var daysInPeriod = periodEnd.DayNumber - periodStart.DayNumber;
        var daysInMonth = DateTime.DaysInMonth(periodStart.Year, periodStart.Month);
        var proRataFactor = (decimal)daysInPeriod / daysInMonth;

        var gridSubscription = request.GridSubscriptionPerMonth * proRataFactor;
        var supplierSubscription = request.SupplierSubscriptionPerMonth * proRataFactor;

        // Round each line to 2 decimal places
        var lines = new List<SettlementLine>
        {
            new("energy", totalKwh, Math.Round(energyTotal, 2)),
            new("grid_tariff", totalKwh, Math.Round(gridTariffTotal, 2)),
            new("system_tariff", totalKwh, Math.Round(systemTotal, 2)),
            new("transmission_tariff", totalKwh, Math.Round(transmissionTotal, 2)),
            new("electricity_tax", totalKwh, Math.Round(electricityTaxTotal, 2)),
            new("grid_subscription", null, Math.Round(gridSubscription, 2)),
            new("supplier_subscription", null, Math.Round(supplierSubscription, 2)),
        };

        var subtotal = lines.Sum(l => l.Amount);
        var vatAmount = Math.Round(subtotal * VatRate, 2);
        var total = subtotal + vatAmount;

        return new SettlementResult(
            request.MeteringPointId, periodStart, periodEnd,
            totalKwh, lines, subtotal, vatAmount, total);
    }
}
