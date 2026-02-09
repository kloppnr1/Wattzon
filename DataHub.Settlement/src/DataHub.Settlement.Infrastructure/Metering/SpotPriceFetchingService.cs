using DataHub.Settlement.Application.Metering;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Metering;

/// <summary>
/// Background service that periodically fetches day-ahead spot prices from Energi Data Service
/// and stores them in the database.
///
/// Runs on startup (to backfill any missing data) and then once per hour.
/// Day-ahead prices for the next day are typically published around 13:00 CET.
/// </summary>
public sealed class SpotPriceFetchingService : BackgroundService
{
    private static readonly string[] PriceAreas = ["DK1", "DK2"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    // How many days ahead to fetch (day-ahead = tomorrow)
    private const int DaysAhead = 2;

    // How many days back to ensure coverage (handles restarts, gaps)
    private const int DaysBack = 7;

    private readonly ISpotPriceProvider _provider;
    private readonly ISpotPriceRepository _repository;
    private readonly ILogger<SpotPriceFetchingService> _logger;

    public SpotPriceFetchingService(
        ISpotPriceProvider provider,
        ISpotPriceRepository repository,
        ILogger<SpotPriceFetchingService> logger)
    {
        _provider = provider;
        _repository = repository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpotPriceFetchingService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchAllPriceAreasAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error fetching spot prices, will retry in {Interval}", PollInterval);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task FetchAllPriceAreasAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-DaysBack);
        var to = today.AddDays(DaysAhead);

        foreach (var priceArea in PriceAreas)
        {
            try
            {
                _logger.LogInformation(
                    "Fetching spot prices for {PriceArea} from {From} to {To}",
                    priceArea, from, to);

                var prices = await _provider.FetchPricesAsync(priceArea, from, to, ct);

                if (prices.Count > 0)
                {
                    await _repository.StorePricesAsync(prices, ct);
                    _logger.LogInformation(
                        "Stored {Count} spot prices for {PriceArea}", prices.Count, priceArea);
                }
                else
                {
                    _logger.LogWarning("No spot prices returned for {PriceArea}", priceArea);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "HTTP error fetching spot prices for {PriceArea}, will retry next cycle", priceArea);
            }
        }
    }
}
