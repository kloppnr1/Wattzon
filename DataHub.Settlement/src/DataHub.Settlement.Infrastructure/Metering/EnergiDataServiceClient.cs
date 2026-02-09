using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataHub.Settlement.Application.Metering;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Metering;

/// <summary>
/// Fetches day-ahead spot prices from Energi Data Service (energidataservice.dk).
/// Free, public, no authentication required.
///
/// Two datasets:
///   - Elspotprices:    hourly (PT1H),          available up to Sep 30 2025
///   - DayAheadPrices:  quarter-hourly (PT15M), available from Oct 1 2025 onward
///
/// Prices are returned in DKK/MWh by the API and converted to øre/kWh for storage.
/// Conversion: DKK/MWh ÷ 10 = øre/kWh  (1 MWh = 1000 kWh, 1 DKK = 100 øre).
/// </summary>
public sealed class EnergiDataServiceClient : ISpotPriceProvider
{
    private const string BaseUrl = "https://api.energidataservice.dk/dataset";
    private const string HourlyDataset = "Elspotprices";
    private const string QuarterHourlyDataset = "DayAheadPrices";

    // The transition date when Nord Pool switched to 15-minute MTU
    private static readonly DateOnly QuarterHourCutover = new(2025, 10, 1);

    private readonly HttpClient _httpClient;
    private readonly ILogger<EnergiDataServiceClient> _logger;

    public EnergiDataServiceClient(HttpClient httpClient, ILogger<EnergiDataServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SpotPriceRow>> FetchPricesAsync(
        string priceArea, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var results = new List<SpotPriceRow>();

        // If the range spans the cutover, split into hourly and quarter-hourly portions
        if (from < QuarterHourCutover)
        {
            var hourlyEnd = to <= QuarterHourCutover ? to : QuarterHourCutover;
            var hourlyPrices = await FetchDatasetAsync(
                HourlyDataset, priceArea, from, hourlyEnd, "PT1H", ct);
            results.AddRange(hourlyPrices);
        }

        if (to > QuarterHourCutover)
        {
            var qhStart = from >= QuarterHourCutover ? from : QuarterHourCutover;
            var qhPrices = await FetchDatasetAsync(
                QuarterHourlyDataset, priceArea, qhStart, to, "PT15M", ct);
            results.AddRange(qhPrices);
        }

        return results;
    }

    private async Task<List<SpotPriceRow>> FetchDatasetAsync(
        string dataset, string priceArea, DateOnly from, DateOnly to,
        string resolution, CancellationToken ct)
    {
        // energidataservice.dk interprets dates in Danish timezone (CET/CEST).
        // We pass dates as yyyy-MM-dd which the API interprets as Danish midnight.
        // The filter parameter must be URL-encoded JSON.
        var filter = JsonSerializer.Serialize(new { PriceArea = new[] { priceArea } });
        var encodedFilter = Uri.EscapeDataString(filter);
        var url = $"{BaseUrl}/{dataset}" +
                  $"?start={from:yyyy-MM-dd}" +
                  $"&end={to:yyyy-MM-dd}" +
                  $"&filter={encodedFilter}" +
                  $"&sort=HourUTC%20asc" +
                  $"&columns=HourUTC,PriceArea,SpotPriceDKK";

        _logger.LogInformation(
            "Fetching {Dataset} prices for {PriceArea} from {From} to {To}",
            dataset, priceArea, from, to);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<EnergiDataServiceResponse>(
            JsonOptions, ct);

        if (body?.Records is null || body.Records.Count == 0)
        {
            _logger.LogWarning(
                "No records returned from {Dataset} for {PriceArea} {From}–{To}",
                dataset, priceArea, from, to);
            return [];
        }

        _logger.LogInformation(
            "Received {Count} price records from {Dataset} for {PriceArea}",
            body.Records.Count, dataset, priceArea);

        var prices = new List<SpotPriceRow>(body.Records.Count);
        foreach (var record in body.Records)
        {
            if (record.SpotPriceDkk is null)
                continue;

            var timestamp = DateTime.SpecifyKind(record.HourUtc, DateTimeKind.Utc);
            var priceOrePerKwh = record.SpotPriceDkk.Value / 10m; // DKK/MWh → øre/kWh

            prices.Add(new SpotPriceRow(record.PriceArea, timestamp, priceOrePerKwh, resolution));
        }

        return prices;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class EnergiDataServiceResponse
    {
        public long Total { get; set; }
        public string? Dataset { get; set; }
        public List<PriceRecord> Records { get; set; } = [];
    }

    private sealed class PriceRecord
    {
        [JsonPropertyName("HourUTC")]
        public DateTime HourUtc { get; set; }

        public string PriceArea { get; set; } = "";

        [JsonPropertyName("SpotPriceDKK")]
        public decimal? SpotPriceDkk { get; set; }
    }
}
