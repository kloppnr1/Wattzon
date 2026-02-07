using Dapper;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class MeteringCompletenessChecker : IMeteringCompletenessChecker
{
    private readonly string _connectionString;

    static MeteringCompletenessChecker()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public MeteringCompletenessChecker(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<MeteringCompleteness> CheckAsync(string gsrn, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        var totalHours = (int)(periodEnd - periodStart).TotalHours;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var receivedHours = await conn.QuerySingleAsync<int>(
            new CommandDefinition("""
                SELECT COUNT(*)::int
                FROM metering.metering_data
                WHERE metering_point_id = @Gsrn
                  AND timestamp >= @Start
                  AND timestamp < @End
                """,
                new { Gsrn = gsrn, Start = periodStart, End = periodEnd },
                cancellationToken: ct));

        return new MeteringCompleteness(totalHours, receivedHours, receivedHours >= totalHours);
    }
}
