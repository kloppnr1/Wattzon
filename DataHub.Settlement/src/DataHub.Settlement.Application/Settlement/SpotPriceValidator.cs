using DataHub.Settlement.Application.Metering;

namespace DataHub.Settlement.Application.Settlement;

public sealed class SpotPriceValidator
{
    public SpotPriceValidationResult Validate(
        IReadOnlyList<SpotPriceRow> spotPrices,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var priceHours = new HashSet<DateTime>(spotPrices.Select(p => p.Hour));
        var missingHours = new List<DateTime>();

        var hour = periodStart;
        while (hour < periodEnd)
        {
            if (!priceHours.Contains(hour))
                missingHours.Add(hour);
            hour = hour.AddHours(1);
        }

        return new SpotPriceValidationResult(missingHours.Count == 0, missingHours);
    }
}

public record SpotPriceValidationResult(bool IsValid, IReadOnlyList<DateTime> MissingHours);
