using Dapper;
using DataHub.Settlement.Application.Metering;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Metering;

public sealed class MeteringDataRepository : IMeteringDataRepository
{
    private readonly string _connectionString;

    static MeteringDataRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public MeteringDataRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task StoreTimeSeriesAsync(string meteringPointId, IReadOnlyList<MeteringDataRow> rows, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO metering.metering_data (metering_point_id, timestamp, resolution, quantity_kwh, quality_code, source_message_id)
            VALUES (@MeteringPointId, @Timestamp, @Resolution, @QuantityKwh, @QualityCode, @SourceMessageId)
            ON CONFLICT (metering_point_id, timestamp) DO UPDATE SET
                quantity_kwh = EXCLUDED.quantity_kwh,
                quality_code = EXCLUDED.quality_code,
                source_message_id = EXCLUDED.source_message_id,
                received_at = now()
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var parameters = rows.Select(r => new
        {
            MeteringPointId = meteringPointId,
            r.Timestamp,
            r.Resolution,
            r.QuantityKwh,
            r.QualityCode,
            r.SourceMessageId,
        });

        await conn.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MeteringDataRow>> GetConsumptionAsync(
        string meteringPointId, DateTime from, DateTime to, CancellationToken ct)
    {
        const string sql = """
            SELECT timestamp, resolution, quantity_kwh, quality_code, source_message_id
            FROM metering.metering_data
            WHERE metering_point_id = @MeteringPointId AND timestamp >= @From AND timestamp < @To
            ORDER BY timestamp
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = await conn.QueryAsync<MeteringDataRow>(
            new CommandDefinition(sql, new { MeteringPointId = meteringPointId, From = from, To = to }, cancellationToken: ct));

        return rows.ToList();
    }
}
