using DataHub.Settlement.Application.Metering;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Metering;

/// <summary>
/// Background service that fetches day-ahead spot prices from Energi Data Service.
///
/// Schedule:
/// - Runs at startup to backfill any missing data.
/// - Targets 13:15 CET daily (shortly after prices are published ~13:00 CET).
/// - If tomorrow's data is obtained, sleeps until next day's 13:15 CET.
/// - If tomorrow's data is missing, retries every 15 minutes until success.
/// </summary>
public sealed class SpotPriceFetchingService : BackgroundService
{
    private static readonly string[] PriceAreas = ["DK1", "DK2"];
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);

    // Target fetch time: 13:15 CET (12:15 UTC in winter, 11:15 UTC in summer)
    private static readonly TimeOnly TargetTimeCet = new(13, 15);

    // How many days ahead to fetch (day-ahead = tomorrow)
    private const int DaysAhead = 2;

    // Initial backfill: 1 month before the hourly→quarter-hour cutover (Oct 1 2025)
    private static readonly DateOnly InitialBackfillFrom = new(2025, 9, 1);

    private static readonly TimeZoneInfo CetZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

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

        // Initial fetch on startup
        var hasTomorrow = false;
        try
        {
            hasTomorrow = await RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial spot price fetch");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = hasTomorrow
                ? GetDelayUntilNextTarget()
                : RetryInterval;

            _logger.LogInformation(
                "Next spot price fetch in {Delay} (hasTomorrow={HasTomorrow})",
                delay, hasTomorrow);

            await Task.Delay(delay, stoppingToken);

            try
            {
                hasTomorrow = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching spot prices, will retry in {Interval}", RetryInterval);
                hasTomorrow = false;
            }
        }
    }

    internal async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = today.AddDays(DaysAhead);
        var tomorrow = today.AddDays(1);
        var hasTomorrow = false;

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

        // Check if we have tomorrow's data across both areas
        var latestDk1 = await _repository.GetLatestPriceDateAsync("DK1", ct);
        var latestDk2 = await _repository.GetLatestPriceDateAsync("DK2", ct);
        hasTomorrow = latestDk1.HasValue && latestDk1.Value >= tomorrow
                   && latestDk2.HasValue && latestDk2.Value >= tomorrow;

        return hasTomorrow;
    }

    /// <summary>
    /// Calculates the delay until the next 13:15 CET. If it's already past 13:15 CET today,
    /// targets tomorrow's 13:15 CET.
    /// </summary>
    private TimeSpan GetDelayUntilNextTarget()
    {
        var nowUtc = DateTime.UtcNow;
        var nowCet = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, CetZone);
        var targetToday = nowCet.Date + TargetTimeCet.ToTimeSpan();

        var targetCet = nowCet < targetToday ? targetToday : targetToday.AddDays(1);
        var targetUtc = TimeZoneInfo.ConvertTimeToUtc(targetCet, CetZone);

        return targetUtc - nowUtc;
    }

    /// <summary>
    /// Determines the start date for fetching based on what's already in the database.
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
