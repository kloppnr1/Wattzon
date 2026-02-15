using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class SettlementEngine : ISettlementEngine
{
    private readonly decimal _vatRate;

    public SettlementEngine(decimal vatRate = 0.25m)
    {
        _vatRate = vatRate;
    }

    public SettlementResult Calculate(SettlementRequest request)
    {
        // Build spot price lookup: if prices are quarter-hourly but consumption is hourly,
        // average the 4 quarter-hour prices per hour. Otherwise, use prices directly.
        var spotPriceLookup = BuildSpotPriceLookup(request.SpotPrices, request.Consumption);
        var gridRateByHour = request.GridTariffRates
            .GroupBy(r => r.HourNumber)
            .ToDictionary(g => g.Key, g => g.Last().PricePerKwh);
        var productionByHour = request.Production?.ToDictionary(p => p.Timestamp, p => p.QuantityKwh);
        var hasSolar = productionByHour is not null;

        decimal totalKwh = 0m;
        decimal totalNetKwh = 0m;
        decimal energyTotal = 0m;
        decimal gridTariffTotal = 0m;
        decimal productionCreditTotal = 0m;

        foreach (var row in request.Consumption)
        {
            var kwh = row.QuantityKwh;
            totalKwh += kwh;

            // Solar: compute net consumption per hour
            var netKwh = kwh;
            if (hasSolar && productionByHour!.TryGetValue(row.Timestamp, out var prodKwh))
                netKwh = kwh - prodKwh;

            if (!hasSolar || netKwh > 0)
            {
                var billableKwh = hasSolar ? netKwh : kwh;
                totalNetKwh += billableKwh;

                // Energy: kWh × (spot + margin + supplement)
                if (!spotPriceLookup.TryGetValue(row.Timestamp, out var spotPrice))
                {
                    throw new InvalidOperationException(
                        $"Missing spot price for {row.Timestamp:yyyy-MM-dd HH:mm} on metering point {request.MeteringPointId}. Settlement cannot proceed with incomplete price data.");
                }
                energyTotal += billableKwh * (spotPrice + request.MarginPerKwh + request.SupplementPerKwh);

                // Grid tariff: kWh × grid_rate[hour_of_day]
                var hourNumber = row.Timestamp.Hour + 1;
                if (!gridRateByHour.TryGetValue(hourNumber, out var gridRate))
                {
                    throw new InvalidOperationException(
                        $"Missing grid tariff rate for hour {hourNumber} on metering point {request.MeteringPointId}. Settlement cannot proceed with incomplete tariff data.");
                }
                gridTariffTotal += billableKwh * gridRate;
            }
            else if (netKwh < 0)
            {
                // Excess production — credit at spot price only (no margin, tariffs, or tax)
                if (!spotPriceLookup.TryGetValue(row.Timestamp, out var spotPrice))
                {
                    throw new InvalidOperationException(
                        $"Missing spot price for {row.Timestamp:yyyy-MM-dd HH:mm} on metering point {request.MeteringPointId}. Settlement cannot proceed with incomplete price data.");
                }
                productionCreditTotal += Math.Abs(netKwh) * spotPrice;
            }
            // netKwh == 0: no charge, no credit
        }

        // For non-solar, totalNetKwh == totalKwh
        if (!hasSolar) totalNetKwh = totalKwh;
        var tariffKwh = hasSolar ? totalNetKwh : totalKwh;

        // Flat tariffs (on net consumption for solar, total consumption otherwise)
        var systemTotal = tariffKwh * request.SystemTariffRate;
        var transmissionTotal = tariffKwh * request.TransmissionTariffRate;
        var electricityTaxTotal = CalculateElectricityTax(request, tariffKwh);

        // Subscriptions (pro rata for partial periods)
        var periodStart = request.PeriodStart;
        var periodEnd = request.PeriodEnd;
        var daysInPeriod = (periodEnd.ToDateTime(TimeOnly.MinValue) - periodStart.ToDateTime(TimeOnly.MinValue)).Days;
        var daysInMonth = DateTime.DaysInMonth(periodStart.Year, periodStart.Month);
        var proRataFactor = (decimal)daysInPeriod / daysInMonth;

        var gridSubscription = request.GridSubscriptionPerMonth * proRataFactor;
        var supplierSubscription = request.SupplierSubscriptionPerMonth * proRataFactor;

        // Round each line to 2 decimal places
        var lines = new List<SettlementLine>
        {
            new("energy", totalKwh, Math.Round(energyTotal, 2)),
            new("grid_tariff", tariffKwh, Math.Round(gridTariffTotal, 2)),
            new("system_tariff", tariffKwh, Math.Round(systemTotal, 2)),
            new("transmission_tariff", tariffKwh, Math.Round(transmissionTotal, 2)),
            new("electricity_tax", tariffKwh, Math.Round(electricityTaxTotal, 2)),
            new("grid_subscription", null, Math.Round(gridSubscription, 2)),
            new("supplier_subscription", null, Math.Round(supplierSubscription, 2)),
        };

        if (hasSolar && productionCreditTotal > 0)
        {
            lines.Add(new("production_credit", null, -Math.Round(productionCreditTotal, 2)));
        }

        var subtotal = lines.Sum(l => l.Amount);
        var vatAmount = Math.Round(subtotal * _vatRate, 2);
        var total = subtotal + vatAmount;

        return new SettlementResult(
            request.MeteringPointId, periodStart, periodEnd,
            totalKwh, lines, subtotal, vatAmount, total);
    }

    /// <summary>
    /// Builds a timestamp → DKK/kWh lookup. If spot prices are quarter-hourly (PT15M)
    /// but consumption is hourly (PT1H), averages the 4 quarter-hour prices per hour.
    /// Prices are converted from øre/kWh to DKK for calculation.
    /// </summary>
    private static Dictionary<DateTime, decimal> BuildSpotPriceLookup(
        IReadOnlyList<SpotPriceRow> spotPrices,
        IReadOnlyList<MeteringDataRow> consumption)
    {
        var hasQuarterHourPrices = spotPrices.Any(p => p.Resolution == "PT15M");
        var hasHourlyConsumption = consumption.Count > 0 && consumption[0].Resolution == "PT1H";

        if (hasQuarterHourPrices && hasHourlyConsumption)
        {
            // Aggregate quarter-hour prices to hourly averages
            return spotPrices
                .GroupBy(p => new DateTime(p.Timestamp.Year, p.Timestamp.Month, p.Timestamp.Day,
                    p.Timestamp.Hour, 0, 0, p.Timestamp.Kind))
                .ToDictionary(
                    g => g.Key,
                    g => g.Average(p => p.PricePerKwh) / 100m); // øre → DKK
        }

        // Direct mapping (hourly prices with hourly consumption, or matching resolutions)
        return spotPrices.ToDictionary(p => p.Timestamp, p => p.PricePerKwh / 100m);
    }

    private static decimal CalculateElectricityTax(SettlementRequest request, decimal tariffKwh)
    {
        if (request.Elvarme is null)
        {
            // Standard: flat rate on billable consumption
            return tariffKwh * request.ElectricityTaxRate;
        }

        // Elvarme: split rate at threshold
        var elvarme = request.Elvarme;
        var cumulative = elvarme.CumulativeKwhBeforePeriod;
        decimal total = 0m;

        foreach (var row in request.Consumption)
        {
            var kwh = row.QuantityKwh;

            if (cumulative >= elvarme.AnnualThreshold)
            {
                // Already above threshold — all at reduced rate
                total += kwh * elvarme.ReducedElectricityTaxRate;
            }
            else if (cumulative + kwh <= elvarme.AnnualThreshold)
            {
                // Still below threshold — all at standard rate
                total += kwh * request.ElectricityTaxRate;
            }
            else
            {
                // Crosses threshold this hour — split
                var belowThreshold = elvarme.AnnualThreshold - cumulative;
                var aboveThreshold = kwh - belowThreshold;
                total += belowThreshold * request.ElectricityTaxRate;
                total += aboveThreshold * elvarme.ReducedElectricityTaxRate;
            }

            cumulative += kwh;
        }

        return total;
    }
}
