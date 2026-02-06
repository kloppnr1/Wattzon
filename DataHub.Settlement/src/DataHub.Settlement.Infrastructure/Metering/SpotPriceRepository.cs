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
            INSERT INTO metering.spot_price (price_area, hour, price_per_kwh)
            VALUES (@PriceArea, @Hour, @PricePerKwh)
            ON CONFLICT (price_area, hour) DO UPDATE SET
                price_per_kwh = EXCLUDED.price_per_kwh,
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
            WHERE price_area = @PriceArea AND hour = @Hour
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        return await conn.QuerySingleAsync<decimal>(
            new CommandDefinition(sql, new { PriceArea = priceArea, Hour = hour }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SpotPriceRow>> GetPricesAsync(
        string priceArea, DateTime from, DateTime to, CancellationToken ct)
    {
        const string sql = """
            SELECT price_area, hour, price_per_kwh
            FROM metering.spot_price
            WHERE price_area = @PriceArea AND hour >= @From AND hour < @To
            ORDER BY hour
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = await conn.QueryAsync<SpotPriceRow>(
            new CommandDefinition(sql, new { PriceArea = priceArea, From = from, To = to }, cancellationToken: ct));

        return rows.ToList();
    }
}
