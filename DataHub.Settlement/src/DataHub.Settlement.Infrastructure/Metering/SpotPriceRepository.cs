using Dapper;
using DataHub.Settlement.Application.Metering;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Metering;

public sealed class SpotPriceRepository : ISpotPriceRepository
{
    private readonly string _connectionString;

    public SpotPriceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task StorePricesAsync(IReadOnlyList<SpotPriceRow> prices, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO metering.spot_price (price_area, "timestamp", price_per_kwh, resolution)
            VALUES (@PriceArea, @Timestamp, @PricePerKwh, @Resolution)
            ON CONFLICT (price_area, "timestamp") DO UPDATE SET
                price_per_kwh = EXCLUDED.price_per_kwh,
                resolution = EXCLUDED.resolution,
                fetched_at = now()
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(sql, prices, cancellationToken: ct));
    }

    public async Task<decimal> GetPriceAsync(string priceArea, DateTime hour, CancellationToken ct)
    {
        const string sql = """
            SELECT price_per_kwh
            FROM metering.spot_price
            WHERE price_area = @PriceArea AND "timestamp" = @Timestamp
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        return await conn.QuerySingleAsync<decimal>(
            new CommandDefinition(sql, new { PriceArea = priceArea, Timestamp = hour }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SpotPriceRow>> GetPricesAsync(
        string priceArea, DateTime from, DateTime to, CancellationToken ct)
    {
        const string sql = """
            SELECT price_area AS PriceArea, "timestamp" AS Timestamp, price_per_kwh AS PricePerKwh, resolution AS Resolution
            FROM metering.spot_price
            WHERE price_area = @PriceArea AND "timestamp" >= @From AND "timestamp" < @To
            ORDER BY "timestamp"
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = await conn.QueryAsync<SpotPriceRow>(
            new CommandDefinition(sql, new { PriceArea = priceArea, From = from, To = to }, cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<SpotPricePagedResult> GetPricesPagedAsync(
        string priceArea, DateTime from, DateTime to, int page, int pageSize, CancellationToken ct)
    {
        const string statsSql = """
            SELECT COUNT(*) AS TotalCount,
                   COALESCE(AVG(price_per_kwh), 0) AS AvgPrice,
                   COALESCE(MIN(price_per_kwh), 0) AS MinPrice,
                   COALESCE(MAX(price_per_kwh), 0) AS MaxPrice
            FROM metering.spot_price
            WHERE price_area = @PriceArea AND "timestamp" >= @From AND "timestamp" < @To
            """;

        const string dataSql = """
            SELECT price_area AS PriceArea, "timestamp" AS Timestamp, price_per_kwh AS PricePerKwh, resolution AS Resolution
            FROM metering.spot_price
            WHERE price_area = @PriceArea AND "timestamp" >= @From AND "timestamp" < @To
            ORDER BY "timestamp"
            LIMIT @Limit OFFSET @Offset
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var args = new { PriceArea = priceArea, From = from, To = to };
        var stats = await conn.QuerySingleAsync<(int TotalCount, decimal AvgPrice, decimal MinPrice, decimal MaxPrice)>(
            new CommandDefinition(statsSql, args, cancellationToken: ct));

        var offset = (page - 1) * pageSize;
        var rows = await conn.QueryAsync<SpotPriceRow>(
            new CommandDefinition(dataSql, new { PriceArea = priceArea, From = from, To = to, Limit = pageSize, Offset = offset }, cancellationToken: ct));

        return new SpotPricePagedResult(rows.ToList(), stats.TotalCount, stats.AvgPrice, stats.MinPrice, stats.MaxPrice);
    }

    public async Task<DateOnly?> GetLatestPriceDateAsync(string priceArea, CancellationToken ct)
    {
        const string sql = """
            SELECT MAX("timestamp")
            FROM metering.spot_price
            WHERE price_area = @PriceArea
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var result = await conn.QuerySingleOrDefaultAsync<DateTime?>(
            new CommandDefinition(sql, new { PriceArea = priceArea }, cancellationToken: ct));

        return result.HasValue ? DateOnly.FromDateTime(result.Value) : null;
    }

    public async Task<DateOnly?> GetEarliestPriceDateAsync(string priceArea, CancellationToken ct)
    {
        const string sql = """
            SELECT MIN("timestamp")
            FROM metering.spot_price
            WHERE price_area = @PriceArea
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var result = await conn.QuerySingleOrDefaultAsync<DateTime?>(
            new CommandDefinition(sql, new { PriceArea = priceArea }, cancellationToken: ct));

        return result.HasValue ? DateOnly.FromDateTime(result.Value) : null;
    }

    public async Task<SpotPriceStatus> GetStatusAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT
                MAX("timestamp") AS LatestTs,
                MAX(fetched_at) AS LastFetchedAt
            FROM metering.spot_price
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var row = await conn.QuerySingleAsync<(DateTime? LatestTs, DateTime? LastFetchedAt)>(
            new CommandDefinition(sql, cancellationToken: ct));

        var now = DateTime.UtcNow;
        var tomorrow = DateOnly.FromDateTime(now).AddDays(1);
        var latestDate = row.LatestTs.HasValue ? DateOnly.FromDateTime(row.LatestTs.Value) : (DateOnly?)null;
        var hasTomorrow = latestDate.HasValue && latestDate.Value >= tomorrow;

        // CET is UTC+1 (or UTC+2 in summer). Use 14:00 UTC as a rough proxy for 15:00 CET.
        var status = hasTomorrow
            ? "ok"
            : now.Hour >= 14
                ? "alert"
                : "warning";

        return new SpotPriceStatus(latestDate, row.LastFetchedAt, hasTomorrow, status);
    }
}
