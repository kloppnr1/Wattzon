using System.Text.Json;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Domain.MasterData;
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

        // Registration timestamp: per-series or fall back to document-level createdDateTime
        var documentCreated = root.TryGetProperty("createdDateTime", out var cdProp)
            ? DateTimeOffset.Parse(cdProp.GetString()!)
            : DateTimeOffset.UtcNow;

        var result = new List<ParsedTimeSeries>();

        foreach (var series in seriesArray.EnumerateArray())
        {
            var transactionId = series.GetProperty("mRID").GetString()!;
            var gsrn = series.GetProperty("MarketEvaluationPoint").GetProperty("mRID").GetString()!;
            var mpType = series.GetProperty("MarketEvaluationPoint").GetProperty("type").GetString()!;

            // Parse registration timestamp (series-level > document-level)
            var registrationTimestamp = documentCreated;
            if (series.TryGetProperty("Registration_DateAndOrTime", out var regProp) &&
                regProp.TryGetProperty("dateTime", out var regDt))
            {
                registrationTimestamp = DateTimeOffset.Parse(regDt.GetString()!);
            }

            var period = series.GetProperty("Period");
            var resolution = period.GetProperty("resolution").GetString()!;
            var interval = period.GetProperty("timeInterval");
            var periodStart = DateTimeOffset.Parse(interval.GetProperty("start").GetString()!);
            var periodEnd = DateTimeOffset.Parse(interval.GetProperty("end").GetString()!);

            var step = GetStep(resolution);
            var points = new List<TimeSeriesPoint>();

            foreach (var point in period.GetProperty("Point").EnumerateArray())
            {
                var position = point.GetProperty("position").GetInt32();
                var timestamp = ComputeTimestamp(periodStart, position, step);

                // Quality is optional — measured data has no quality element
                string? quality = null;
                if (point.TryGetProperty("quality", out var qualProp))
                {
                    quality = qualProp.GetString();
                }

                decimal quantity = 0m;
                if (point.TryGetProperty("quantity", out var qProp))
                {
                    quantity = qProp.GetDecimal();
                }

                points.Add(new TimeSeriesPoint(position, timestamp, quantity, quality));
            }

            result.Add(new ParsedTimeSeries(
                transactionId, gsrn, mpType, resolution,
                periodStart, periodEnd, registrationTimestamp, points));
        }

        return result;
    }

    private static readonly Dictionary<string, string> SettlementMethodMap = new()
    {
        ["D01"] = "flex",
        ["E02"] = "non_profiled",
    };

    private static readonly Dictionary<string, string> GridAreaToPriceArea = new()
    {
        ["344"] = "DK1",
        ["347"] = "DK1",
        ["348"] = "DK1",
        ["351"] = "DK1",
        ["357"] = "DK1",
        ["370"] = "DK1",
        ["740"] = "DK2",
        ["757"] = "DK2",
        ["791"] = "DK2",
        ["853"] = "DK2",
        ["854"] = "DK2",
        ["860"] = "DK2",
    };

    public ParsedMasterData ParseRsm022(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("MarketDocument");
        var messageId = root.GetProperty("mRID").GetString()!;

        var activity = root.GetProperty("MktActivityRecord");
        var mp = activity.GetProperty("MarketEvaluationPoint");

        var gsrn = mp.GetProperty("mRID").GetString()!;
        var type = mp.GetProperty("type").GetString()!;
        var settlementMethodCode = mp.GetProperty("settlementMethod").GetString()!;
        var settlementMethod = SettlementMethodMap.GetValueOrDefault(settlementMethodCode, settlementMethodCode);
        var gridAreaCode = mp.GetProperty("linkedMarketEvaluationPoint").GetProperty("mRID").GetString()!;
        var gridOperatorGln = mp.GetProperty("inDomain").GetProperty("mRID").GetString()!;
        var priceArea = GridAreaToPriceArea.GetValueOrDefault(gridAreaCode, "DK1");

        var supplyStart = DateTimeOffset.Parse(
            activity.GetProperty("Period").GetProperty("timeInterval").GetProperty("start").GetString()!);

        return new ParsedMasterData(messageId, gsrn, type, settlementMethod, gridAreaCode, gridOperatorGln, priceArea, supplyStart);
    }

    public Rsm004Result ParseRsm004(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("MarketDocument");

        var activity = root.GetProperty("MktActivityRecord");
        var mp = activity.GetProperty("MarketEvaluationPoint");

        var gsrn = mp.GetProperty("mRID").GetString()!;

        string? newGridAreaCode = null;
        if (mp.TryGetProperty("linkedMarketEvaluationPoint", out var linkedMp))
        {
            newGridAreaCode = linkedMp.GetProperty("mRID").GetString();
        }

        string? newSettlementMethod = null;
        if (mp.TryGetProperty("settlementMethod", out var sm))
        {
            var code = sm.GetString()!;
            newSettlementMethod = SettlementMethodMap.GetValueOrDefault(code, code);
        }

        string? newConnectionStatus = null;
        if (mp.TryGetProperty("connectionState", out var cs))
        {
            newConnectionStatus = cs.GetString();
        }

        var effectiveDate = DateTimeOffset.Parse(
            activity.GetProperty("Period").GetProperty("timeInterval").GetProperty("start").GetString()!);

        string? reasonCode = null;
        if (activity.TryGetProperty("Reason", out var reason) &&
            reason.TryGetProperty("code", out var rc))
        {
            reasonCode = rc.GetString();
        }

        return new Rsm004Result(gsrn, newGridAreaCode, newSettlementMethod, newConnectionStatus, effectiveDate, reasonCode);
    }

    public Rsm014Aggregation ParseRsm014(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("MarketDocument");

        var series = root.GetProperty("Series");
        var firstSeries = series.EnumerateArray().First();

        var gridAreaCode = firstSeries.GetProperty("marketEvaluationPoint")
            .GetProperty("meteringGridArea").GetProperty("mRID").GetString()!;

        var period = firstSeries.GetProperty("Period");
        var interval = period.GetProperty("timeInterval");
        var periodStart = DateTimeOffset.Parse(interval.GetProperty("start").GetString()!);
        var periodEnd = DateTimeOffset.Parse(interval.GetProperty("end").GetString()!);
        var resolution = period.GetProperty("resolution").GetString()!;
        var step = GetStep(resolution);

        var points = new List<AggregationPoint>();
        decimal totalKwh = 0m;

        foreach (var point in period.GetProperty("Point").EnumerateArray())
        {
            var position = point.GetProperty("position").GetInt32();
            var timestamp = ComputeTimestamp(periodStart, position, step);
            var quantity = point.GetProperty("quantity").GetDecimal();

            points.Add(new AggregationPoint(timestamp.UtcDateTime, quantity));
            totalKwh += quantity;
        }

        return new Rsm014Aggregation(
            gridAreaCode,
            DateOnly.FromDateTime(periodStart.UtcDateTime),
            DateOnly.FromDateTime(periodEnd.UtcDateTime),
            totalKwh,
            points);
    }

    public Rsm028Result ParseRsm028(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("MarketDocument");
        var messageId = root.GetProperty("mRID").GetString()!;

        var activity = root.GetProperty("MktActivityRecord");
        var mp = activity.GetProperty("MarketEvaluationPoint");
        var gsrn = mp.GetProperty("mRID").GetString()!;

        var customer = activity.GetProperty("Customer");
        var customerName = customer.GetProperty("name").GetString()!;
        var cprCvr = customer.TryGetProperty("mRID", out var mrid) ? mrid.GetString()! : "";
        var customerType = customer.TryGetProperty("type", out var ct) ? ct.GetString()! : "person";

        string? phone = null;
        if (customer.TryGetProperty("phone", out var ph))
            phone = ph.GetString();

        string? email = null;
        if (customer.TryGetProperty("email", out var em))
            email = em.GetString();

        return new Rsm028Result(messageId, gsrn, customerName, cprCvr, customerType, phone, email);
    }

    public Rsm031Result ParseRsm031(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("MarketDocument");
        var messageId = root.GetProperty("mRID").GetString()!;

        var activity = root.GetProperty("MktActivityRecord");
        var mp = activity.GetProperty("MarketEvaluationPoint");
        var gsrn = mp.GetProperty("mRID").GetString()!;

        var tariffs = new List<TariffAttachment>();
        if (activity.TryGetProperty("ChargeType", out var chargeTypes))
        {
            foreach (var charge in chargeTypes.EnumerateArray())
            {
                var tariffId = charge.GetProperty("mRID").GetString()!;
                var tariffType = charge.TryGetProperty("type", out var tt) ? tt.GetString()! : "grid";
                var validFrom = DateOnly.Parse(charge.GetProperty("effectiveDate").GetString()!.Split('T')[0]);
                DateOnly? validTo = null;
                if (charge.TryGetProperty("terminationDate", out var td) && td.ValueKind == JsonValueKind.String)
                    validTo = DateOnly.Parse(td.GetString()!.Split('T')[0]);

                tariffs.Add(new TariffAttachment(tariffId, tariffType, validFrom, validTo));
            }
        }

        return new Rsm031Result(messageId, gsrn, tariffs);
    }

    public Rsm001ResponseResult ParseRsm005Response(string json)
        => ParseRsm001Response(json); // RSM-005 has identical accept/reject structure

    public Rsm001ResponseResult ParseRsm024Response(string json)
        => ParseRsm001Response(json); // RSM-024 response has identical accept/reject structure

    public Rsm001ResponseResult ParseRsm001Response(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("MarketDocument");

        var correlationId = root.GetProperty("mRID").GetString()!;

        var activity = root.GetProperty("MktActivityRecord");
        var statusCode = activity.GetProperty("status").GetProperty("value").GetString()!;

        // A01 = accepted, A02 = rejected
        var accepted = statusCode == "A01";

        string? rejectionReason = null;
        string? rejectionCode = null;
        if (!accepted && activity.TryGetProperty("Reason", out var reason))
        {
            rejectionCode = reason.TryGetProperty("code", out var code) ? code.GetString() : null;
            rejectionReason = reason.TryGetProperty("text", out var text) ? text.GetString() : null;
        }

        return new Rsm001ResponseResult(correlationId, accepted, rejectionReason, rejectionCode);
    }

    public GridTariffResult ParseGridTariff(string json)
    {
        using var doc = JsonDocument.Parse(json);

        var tariff = doc.RootElement.GetProperty("GridTariff");
        var gridAreaCode = tariff.GetProperty("gridAreaCode").GetString()!;
        var chargeOwnerId = tariff.GetProperty("chargeOwnerId").GetString()!;
        var validFrom = DateOnly.Parse(tariff.GetProperty("validFrom").GetString()!);
        var tariffType = tariff.GetProperty("tariffType").GetString()!;

        var rates = new List<TariffRateRow>();
        foreach (var rate in tariff.GetProperty("rates").EnumerateArray())
        {
            var hour = rate.GetProperty("hour").GetInt32();
            var price = rate.GetProperty("pricePerKwh").GetDecimal();
            rates.Add(new TariffRateRow(hour, price));
        }

        var sub = doc.RootElement.GetProperty("Subscription");
        var subscriptionType = sub.GetProperty("subscriptionType").GetString()!;
        var amountPerMonth = sub.GetProperty("amountPerMonth").GetDecimal();

        return new GridTariffResult(gridAreaCode, chargeOwnerId, validFrom, tariffType, rates, subscriptionType, amountPerMonth);
    }

    private static TimeSpan GetStep(string resolution)
    {
        if (ResolutionMap.TryGetValue(resolution, out var step))
            return step;

        throw new ArgumentException($"Unknown resolution: {resolution}");
    }

    private static DateTimeOffset ComputeTimestamp(
        DateTimeOffset periodStart, int position, TimeSpan step)
    {
        return periodStart + (position - 1) * step;
    }
}
