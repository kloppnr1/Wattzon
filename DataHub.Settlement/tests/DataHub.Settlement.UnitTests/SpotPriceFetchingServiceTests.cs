using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Infrastructure.Metering;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class SpotPriceFetchingServiceTests
{
    private readonly FakeSpotPriceProvider _provider = new();
    private readonly FakeSpotPriceRepository _repository = new();

    private SpotPriceFetchingService CreateService() =>
        new(_provider, _repository, NullLogger<SpotPriceFetchingService>.Instance);

    [Fact]
    public async Task No_data_in_db_fetches_from_initial_backfill_date()
    {
        // No data in the repository at all
        var sut = CreateService();

        await sut.RunOnceAsync(CancellationToken.None);

        // Should have fetched for both DK1 and DK2 from Sep 1 2025
        _provider.Calls.Should().HaveCount(2);
        foreach (var call in _provider.Calls)
        {
            call.From.Should().Be(new DateOnly(2025, 9, 1));
        }
    }

    [Fact]
    public async Task Existing_data_fetches_incrementally_from_latest_date()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var threeDaysAgo = today.AddDays(-3);

        // Simulate existing data: earliest from Sep 1, latest 3 days ago
        _repository.EarliestDates["DK1"] = new DateOnly(2025, 9, 1);
        _repository.EarliestDates["DK2"] = new DateOnly(2025, 9, 1);
        _repository.LatestDates["DK1"] = threeDaysAgo;
        _repository.LatestDates["DK2"] = threeDaysAgo;

        var sut = CreateService();

        // First run — marks initial load done
        await sut.RunOnceAsync(CancellationToken.None);
        _provider.Calls.Clear();

        // Second run — should be incremental
        _repository.LatestDates["DK1"] = threeDaysAgo;
        _repository.LatestDates["DK2"] = threeDaysAgo;

        await sut.RunOnceAsync(CancellationToken.None);

        _provider.Calls.Should().HaveCount(2);
        foreach (var call in _provider.Calls)
        {
            // Latest data is threeDaysAgo, so incremental fetch starts from the next day
            call.From.Should().Be(threeDaysAgo.AddDays(1));
            call.To.Should().Be(today.AddDays(2));
        }
    }

    [Fact]
    public async Task First_run_with_gap_at_start_backfills_from_initial_date()
    {
        // Data exists but starts after InitialBackfillFrom (gap at beginning)
        _repository.EarliestDates["DK1"] = new DateOnly(2025, 10, 1);
        _repository.EarliestDates["DK2"] = new DateOnly(2025, 10, 1);
        _repository.LatestDates["DK1"] = new DateOnly(2025, 10, 15);
        _repository.LatestDates["DK2"] = new DateOnly(2025, 10, 15);

        var sut = CreateService();

        await sut.RunOnceAsync(CancellationToken.None);

        _provider.Calls.Should().HaveCount(2);
        foreach (var call in _provider.Calls)
        {
            call.From.Should().Be(new DateOnly(2025, 9, 1));
        }
    }

    [Fact]
    public async Task Skips_fetch_when_already_up_to_date()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureDate = today.AddDays(5); // well beyond DaysAhead (2)

        _repository.EarliestDates["DK1"] = new DateOnly(2025, 9, 1);
        _repository.EarliestDates["DK2"] = new DateOnly(2025, 9, 1);
        _repository.LatestDates["DK1"] = futureDate;
        _repository.LatestDates["DK2"] = futureDate;

        var sut = CreateService();

        await sut.RunOnceAsync(CancellationToken.None);

        // Provider should not have been called — data is already beyond the lookahead
        _provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Subsequent_runs_do_not_check_earliest_date()
    {
        // Set up with data that has a gap at the start
        _repository.EarliestDates["DK1"] = new DateOnly(2025, 10, 1); // after InitialBackfillFrom
        _repository.EarliestDates["DK2"] = new DateOnly(2025, 10, 1);
        _repository.LatestDates["DK1"] = DateOnly.FromDateTime(DateTime.UtcNow);
        _repository.LatestDates["DK2"] = DateOnly.FromDateTime(DateTime.UtcNow);

        var sut = CreateService();

        // First run: backfills because of the gap
        await sut.RunOnceAsync(CancellationToken.None);
        _provider.Calls.Should().AllSatisfy(c =>
            c.From.Should().Be(new DateOnly(2025, 9, 1)));

        _provider.Calls.Clear();
        _repository.EarliestCheckCount = 0;

        // Second run: should NOT check earliest anymore, just use latest
        await sut.RunOnceAsync(CancellationToken.None);

        _repository.EarliestCheckCount.Should().Be(0,
            "subsequent runs should skip the earliest-date check");
    }

    [Fact]
    public async Task Survives_timeout_on_first_price_area_and_continues_to_second()
    {
        _repository.LatestDates["DK1"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        _repository.LatestDates["DK2"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        _repository.EarliestDates["DK1"] = new DateOnly(2025, 9, 1);
        _repository.EarliestDates["DK2"] = new DateOnly(2025, 9, 1);

        // DK1 will throw a timeout (TaskCanceledException), DK2 should still be fetched
        var timeoutProvider = new TimeoutOnFirstCallProvider();
        var sut = new SpotPriceFetchingService(
            timeoutProvider, _repository, NullLogger<SpotPriceFetchingService>.Instance);

        await sut.RunOnceAsync(CancellationToken.None);

        // DK1 timed out, but DK2 should have been fetched
        timeoutProvider.SuccessfulCalls.Should().ContainSingle()
            .Which.PriceArea.Should().Be("DK2");
    }

    [Fact]
    public async Task Survives_http_error_on_first_price_area_and_continues_to_second()
    {
        _repository.LatestDates["DK1"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        _repository.LatestDates["DK2"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        _repository.EarliestDates["DK1"] = new DateOnly(2025, 9, 1);
        _repository.EarliestDates["DK2"] = new DateOnly(2025, 9, 1);

        var errorProvider = new HttpErrorOnFirstCallProvider();
        var sut = new SpotPriceFetchingService(
            errorProvider, _repository, NullLogger<SpotPriceFetchingService>.Instance);

        await sut.RunOnceAsync(CancellationToken.None);

        errorProvider.SuccessfulCalls.Should().ContainSingle()
            .Which.PriceArea.Should().Be("DK2");
    }

    // ── Fakes ──

    private sealed class FakeSpotPriceProvider : ISpotPriceProvider
    {
        public List<(string PriceArea, DateOnly From, DateOnly To)> Calls { get; } = [];

        public Task<IReadOnlyList<SpotPriceRow>> FetchPricesAsync(
            string priceArea, DateOnly from, DateOnly to, CancellationToken ct)
        {
            Calls.Add((priceArea, from, to));
            return Task.FromResult<IReadOnlyList<SpotPriceRow>>([]);
        }
    }

    private sealed class TimeoutOnFirstCallProvider : ISpotPriceProvider
    {
        private bool _firstCall = true;
        public List<(string PriceArea, DateOnly From, DateOnly To)> SuccessfulCalls { get; } = [];

        public Task<IReadOnlyList<SpotPriceRow>> FetchPricesAsync(
            string priceArea, DateOnly from, DateOnly to, CancellationToken ct)
        {
            if (_firstCall)
            {
                _firstCall = false;
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout",
                    new TimeoutException());
            }
            SuccessfulCalls.Add((priceArea, from, to));
            return Task.FromResult<IReadOnlyList<SpotPriceRow>>([]);
        }
    }

    private sealed class HttpErrorOnFirstCallProvider : ISpotPriceProvider
    {
        private bool _firstCall = true;
        public List<(string PriceArea, DateOnly From, DateOnly To)> SuccessfulCalls { get; } = [];

        public Task<IReadOnlyList<SpotPriceRow>> FetchPricesAsync(
            string priceArea, DateOnly from, DateOnly to, CancellationToken ct)
        {
            if (_firstCall)
            {
                _firstCall = false;
                throw new HttpRequestException("Service unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable);
            }
            SuccessfulCalls.Add((priceArea, from, to));
            return Task.FromResult<IReadOnlyList<SpotPriceRow>>([]);
        }
    }

    private sealed class FakeSpotPriceRepository : ISpotPriceRepository
    {
        public Dictionary<string, DateOnly> LatestDates { get; } = new();
        public Dictionary<string, DateOnly> EarliestDates { get; } = new();
        public int EarliestCheckCount { get; set; }
        public List<SpotPriceRow> StoredPrices { get; } = [];

        public Task StorePricesAsync(IReadOnlyList<SpotPriceRow> prices, CancellationToken ct)
        {
            StoredPrices.AddRange(prices);
            return Task.CompletedTask;
        }

        public Task<decimal> GetPriceAsync(string priceArea, DateTime hour, CancellationToken ct)
            => Task.FromResult(0m);

        public Task<IReadOnlyList<SpotPriceRow>> GetPricesAsync(
            string priceArea, DateTime from, DateTime to, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SpotPriceRow>>([]);

        public Task<SpotPricePagedResult> GetPricesPagedAsync(
            string priceArea, DateTime from, DateTime to, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(new SpotPricePagedResult([], 0, 0, 0, 0));

        public Task<DateOnly?> GetLatestPriceDateAsync(string priceArea, CancellationToken ct)
        {
            LatestDates.TryGetValue(priceArea, out var date);
            return Task.FromResult<DateOnly?>(date == default ? null : date);
        }

        public Task<DateOnly?> GetEarliestPriceDateAsync(string priceArea, CancellationToken ct)
        {
            EarliestCheckCount++;
            EarliestDates.TryGetValue(priceArea, out var date);
            return Task.FromResult<DateOnly?>(date == default ? null : date);
        }

        public Task<SpotPriceDualResult> GetPricesByDateAsync(DateOnly date, CancellationToken ct)
            => Task.FromResult(new SpotPriceDualResult([], 0, 0, 0, 0, 0, 0, 0));

        public Task<SpotPriceStatus> GetStatusAsync(CancellationToken ct)
            => Task.FromResult(new SpotPriceStatus(null, null, false, "warning"));
    }
}
