using DataHub.Settlement.Application.Metering;

namespace DataHub.Settlement.Application.Settlement;

public sealed class SpotPriceValidator
{
    public SpotPriceValidationResult Validate(
        IReadOnlyList<SpotPriceRow> spotPrices,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var resolution = spotPrices.Count > 0 ? spotPrices[0].Resolution : "PT1H";
        var step = resolution == "PT15M" ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1);

        var priceTimestamps = new HashSet<DateTime>(spotPrices.Select(p => p.Timestamp));
        var missingSlots = new List<DateTime>();

        var current = periodStart;
        while (current < periodEnd)
        {
            if (!priceTimestamps.Contains(current))
                missingSlots.Add(current);
            current = current.Add(step);
        }

        return new SpotPriceValidationResult(missingSlots.Count == 0, missingSlots);
    }
}

public record SpotPriceValidationResult(bool IsValid, IReadOnlyList<DateTime> MissingSlots);
