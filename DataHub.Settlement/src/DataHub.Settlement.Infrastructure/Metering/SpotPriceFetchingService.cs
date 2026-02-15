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
///
/// Uses incremental fetching: only requests data for periods not already present in the database.
/// </summary>
public sealed class SpotPriceFetchingService : BackgroundService
{
    private static readonly string[] PriceAreas = ["DK1", "DK2"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    // How many days ahead to fetch (day-ahead = tomorrow)
    private const int DaysAhead = 2;

    // Initial backfill: 1 month before the hourly→quarter-hour cutover (Oct 1 2025)
    private static readonly DateOnly InitialBackfillFrom = new(2025, 9, 1);

    private readonly ISpotPriceProvider _provider;
    private readonly ISpotPriceRepository _repository;
    private readonly ILogger<SpotPriceFetchingService> _logger;

    private bool _initialLoadDone;

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
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // Normal shutdown — let it propagate
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching spot prices, will retry in {Interval}", PollInterval);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = today.AddDays(DaysAhead);

        foreach (var priceArea in PriceAreas)
        {
            try
            {
                var from = await DetermineFromDateAsync(priceArea, ct);

                if (from >= to)
                {
                    _logger.LogInformation(
                        "Spot prices for {PriceArea} already up to date through {To}",
                        priceArea, to);
                    continue;
                }

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
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Timeout fetching spot prices for {PriceArea}, will retry next cycle", priceArea);
            }
        }

        _initialLoadDone = true;
    }

    /// <summary>
    /// Determines the start date for fetching based on what's already in the database.
    /// - No data at all → full backfill from <see cref="InitialBackfillFrom"/>.
    /// - First run, earliest data starts after backfill date → backfill from start.
    /// - Otherwise → incremental from the latest date in DB (re-fetches that day
    ///   in case it's incomplete, e.g. not all day-ahead hours published yet).
    /// </summary>
    internal async Task<DateOnly> DetermineFromDateAsync(string priceArea, CancellationToken ct)
    {
        var latest = await _repository.GetLatestPriceDateAsync(priceArea, ct);

        if (latest is null)
        {
            _logger.LogInformation(
                "No existing data for {PriceArea}, backfilling from {From}",
                priceArea, InitialBackfillFrom);
            return InitialBackfillFrom;
        }

        if (!_initialLoadDone)
        {
            var earliest = await _repository.GetEarliestPriceDateAsync(priceArea, ct);
            if (earliest is not null && earliest > InitialBackfillFrom)
            {
                _logger.LogInformation(
                    "Initial backfill needed for {PriceArea}: earliest data is {Earliest}, backfilling from {From}",
                    priceArea, earliest, InitialBackfillFrom);
                return InitialBackfillFrom;
            }
        }

        return latest.Value;
    }
}
