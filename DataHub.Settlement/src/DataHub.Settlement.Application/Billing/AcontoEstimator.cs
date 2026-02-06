using DataHub.Settlement.Application.Eloverblik;

namespace DataHub.Settlement.Application.Billing;

public static class AcontoEstimator
{
    /// <summary>
    /// Estimates a quarterly aconto payment based on historical consumption and expected price per kWh.
    /// Includes fixed monthly subscriptions (grid + supplier) as required by the wholesale model.
    /// Returns the estimated quarterly DKK amount (including VAT).
    /// </summary>
    public static decimal EstimateQuarterlyAmount(
        IReadOnlyList<MonthlyConsumption> history,
        decimal expectedPricePerKwh,
        decimal gridSubscriptionPerMonth = 0m,
        decimal supplierSubscriptionPerMonth = 0m,
        decimal vatRate = 0.25m)
    {
        if (history.Count == 0)
            return 0m;

        var totalKwh = history.Sum(h => h.TotalKwh);
        var monthlyAverageKwh = totalKwh / history.Count;
        var quarterlyKwh = monthlyAverageKwh * 3;
        var variableCosts = quarterlyKwh * expectedPricePerKwh;
        var quarterlySubscriptions = (gridSubscriptionPerMonth + supplierSubscriptionPerMonth) * 3;
        var subtotal = variableCosts + quarterlySubscriptions;
        var withVat = subtotal * (1 + vatRate);

        return Math.Round(withVat, 2);
    }

    /// <summary>
    /// Calculates the expected total price per kWh (DKK) from tariff rates, margin, and spot average.
    /// </summary>
    public static decimal CalculateExpectedPricePerKwh(
        decimal averageSpotPriceOrePerKwh,
        decimal marginOrePerKwh,
        decimal systemTariffRate,
        decimal transmissionTariffRate,
        decimal electricityTaxRate,
        decimal averageGridTariffRate)
    {
        var spotDkk = averageSpotPriceOrePerKwh / 100m;
        var marginDkk = marginOrePerKwh / 100m;

        return spotDkk + marginDkk + systemTariffRate + transmissionTariffRate
            + electricityTaxRate + averageGridTariffRate;
    }
}
