using System.Text.Json;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Domain.Metering;

namespace DataHub.Settlement.Infrastructure.Parsing;

public sealed class CimJsonParser : ICimParser
{
    private static readonly Dictionary<string, TimeSpan> ResolutionMap = new()
    {
        ["PT15M"] = TimeSpan.FromMinutes(15),
        ["PT1H"] = TimeSpan.FromHours(1),
    };

    public IReadOnlyList<ParsedTimeSeries> ParseRsm012(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("MarketDocument");
        var seriesArray = root.GetProperty("Series");

        var result = new List<ParsedTimeSeries>();

        foreach (var series in seriesArray.EnumerateArray())
        {
            var transactionId = series.GetProperty("mRID").GetString()!;
            var gsrn = series.GetProperty("MarketEvaluationPoint").GetProperty("mRID").GetString()!;
            var mpType = series.GetProperty("MarketEvaluationPoint").GetProperty("type").GetString()!;

            var period = series.GetProperty("Period");
            var resolution = period.GetProperty("resolution").GetString()!;
            var interval = period.GetProperty("timeInterval");
            var periodStart = DateTimeOffset.Parse(interval.GetProperty("start").GetString()!);
            var periodEnd = DateTimeOffset.Parse(interval.GetProperty("end").GetString()!);

            var step = GetStep(resolution, periodStart, periodEnd);
            var points = new List<TimeSeriesPoint>();

            foreach (var point in period.GetProperty("Point").EnumerateArray())
            {
                var position = point.GetProperty("position").GetInt32();
                var timestamp = ComputeTimestamp(periodStart, position, step, resolution);
                var quality = point.GetProperty("quality").GetString()!;

                decimal quantity = 0m;
                if (point.TryGetProperty("quantity", out var qProp))
                {
                    quantity = qProp.GetDecimal();
                }

                points.Add(new TimeSeriesPoint(position, timestamp, quantity, quality));
            }

            result.Add(new ParsedTimeSeries(
                transactionId, gsrn, mpType, resolution,
                periodStart, periodEnd, points));
        }

        return result;
    }

    private static TimeSpan GetStep(string resolution, DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        if (ResolutionMap.TryGetValue(resolution, out var step))
            return step;

        if (resolution == "P1M")
            return periodEnd - periodStart; // single point for monthly

        throw new ArgumentException($"Unknown resolution: {resolution}");
    }

    private static DateTimeOffset ComputeTimestamp(
        DateTimeOffset periodStart, int position, TimeSpan step, string resolution)
    {
        if (resolution == "P1M")
            return periodStart;

        return periodStart + (position - 1) * step;
    }
}
