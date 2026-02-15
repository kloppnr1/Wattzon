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
/// Two datasets with different column names:
///   - Elspotprices:    hourly (PT1H),          up to Sep 30 2025
///       Columns: HourUTC, PriceArea, SpotPriceDKK
///   - DayAheadPrices:  quarter-hourly (PT15M), from Oct 1 2025
///       Columns: TimeUTC, PriceArea, DayAheadPriceDKK
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
            var hourlyPrices = await FetchHourlyAsync(priceArea, from, hourlyEnd, ct);
            results.AddRange(hourlyPrices);
        }

        if (to > QuarterHourCutover)
        {
            var qhStart = from >= QuarterHourCutover ? from : QuarterHourCutover;
            var qhPrices = await FetchQuarterHourlyAsync(priceArea, qhStart, to, ct);
            results.AddRange(qhPrices);
        }

        return results;
    }

    /// <summary>Elspotprices dataset: HourUTC, PriceArea, SpotPriceDKK</summary>
    private async Task<List<SpotPriceRow>> FetchHourlyAsync(
        string priceArea, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var encodedFilter = Uri.EscapeDataString(
            JsonSerializer.Serialize(new { PriceArea = new[] { priceArea } }));

        var url = $"{BaseUrl}/{HourlyDataset}" +
                  $"?start={from:yyyy-MM-dd}" +
                  $"&end={to:yyyy-MM-dd}" +
                  $"&filter={encodedFilter}" +
                  $"&sort=HourUTC%20asc" +
                  $"&columns=HourUTC,PriceArea,SpotPriceDKK" +
                  $"&limit=0";

        _logger.LogInformation(
            "Fetching {Dataset} prices for {PriceArea} from {From} to {To}",
            HourlyDataset, priceArea, from, to);

        var body = await GetAsync<ElspotpricesResponse>(url, ct);
        if (body?.Records is null || body.Records.Count == 0)
        {
            _logger.LogWarning("No records from Elspotprices for {PriceArea} {From}–{To}", priceArea, from, to);
            return [];
        }

        _logger.LogInformation("Received {Count} hourly prices for {PriceArea}", body.Records.Count, priceArea);

        return body.Records
            .Where(r => r.SpotPriceDkk is not null)
            .Select(r => new SpotPriceRow(
                r.PriceArea,
                DateTime.SpecifyKind(r.HourUtc, DateTimeKind.Utc),
                r.SpotPriceDkk!.Value / 10m,
                "PT1H"))
            .ToList();
    }

    /// <summary>DayAheadPrices dataset: TimeUTC, PriceArea, DayAheadPriceDKK</summary>
    private async Task<List<SpotPriceRow>> FetchQuarterHourlyAsync(
        string priceArea, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var encodedFilter = Uri.EscapeDataString(
            JsonSerializer.Serialize(new { PriceArea = new[] { priceArea } }));

        var url = $"{BaseUrl}/{QuarterHourlyDataset}" +
                  $"?start={from:yyyy-MM-dd}" +
                  $"&end={to:yyyy-MM-dd}" +
                  $"&filter={encodedFilter}" +
                  $"&sort=TimeUTC%20asc" +
                  $"&columns=TimeUTC,PriceArea,DayAheadPriceDKK" +
                  $"&limit=0";

        _logger.LogInformation(
            "Fetching {Dataset} prices for {PriceArea} from {From} to {To}",
            QuarterHourlyDataset, priceArea, from, to);

        var body = await GetAsync<DayAheadPricesResponse>(url, ct);
        if (body?.Records is null || body.Records.Count == 0)
        {
            _logger.LogWarning("No records from DayAheadPrices for {PriceArea} {From}–{To}", priceArea, from, to);
            return [];
        }

        _logger.LogInformation("Received {Count} quarter-hourly prices for {PriceArea}", body.Records.Count, priceArea);

        return body.Records
            .Where(r => r.DayAheadPriceDkk is not null)
            .Select(r => new SpotPriceRow(
                r.PriceArea,
                DateTime.SpecifyKind(r.TimeUtc, DateTimeKind.Utc),
                r.DayAheadPriceDkk!.Value / 10m,
                "PT15M"))
            .ToList();
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Elspotprices response models ──

    private sealed class ElspotpricesResponse
    {
        public long Total { get; set; }
        public List<ElspotpricesRecord> Records { get; set; } = [];
    }

    private sealed class ElspotpricesRecord
    {
        [JsonPropertyName("HourUTC")]
        public DateTime HourUtc { get; set; }

        public string PriceArea { get; set; } = "";

        [JsonPropertyName("SpotPriceDKK")]
        public decimal? SpotPriceDkk { get; set; }
    }

    // ── DayAheadPrices response models ──

    private sealed class DayAheadPricesResponse
    {
        public long Total { get; set; }
        public List<DayAheadPricesRecord> Records { get; set; } = [];
    }

    private sealed class DayAheadPricesRecord
    {
        [JsonPropertyName("TimeUTC")]
        public DateTime TimeUtc { get; set; }

        public string PriceArea { get; set; } = "";

        [JsonPropertyName("DayAheadPriceDKK")]
        public decimal? DayAheadPriceDkk { get; set; }
    }
}
