using Dapper;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Metering;

public sealed class AnnualConsumptionTracker
{
    private readonly string _connectionString;

    public AnnualConsumptionTracker(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<decimal> GetCumulativeKwhAsync(string meteringPointId, int year, CancellationToken ct)
    {
        const string sql = """
            SELECT COALESCE(cumulative_kwh, 0)
            FROM metering.annual_consumption_tracker
            WHERE metering_point_id = @MeteringPointId AND year = @Year
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<decimal>(
            new CommandDefinition(sql, new { MeteringPointId = meteringPointId, Year = year }, cancellationToken: ct));
    }

    public async Task UpdateCumulativeKwhAsync(string meteringPointId, int year, decimal additionalKwh, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO metering.annual_consumption_tracker (metering_point_id, year, cumulative_kwh)
            VALUES (@MeteringPointId, @Year, @AdditionalKwh)
            ON CONFLICT (metering_point_id, year) DO UPDATE SET
                cumulative_kwh = metering.annual_consumption_tracker.cumulative_kwh + EXCLUDED.cumulative_kwh,
                updated_at = now()
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { MeteringPointId = meteringPointId, Year = year, AdditionalKwh = additionalKwh }, cancellationToken: ct));
    }
}
