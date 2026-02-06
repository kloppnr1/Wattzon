using Dapper;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Tariff;

public sealed class TariffRepository : ITariffRepository
{
    private readonly string _connectionString;

    static TariffRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public TariffRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<TariffRateRow>> GetRatesAsync(
        string gridAreaCode, string tariffType, DateOnly date, CancellationToken ct)
    {
        const string sql = """
            SELECT tr.hour_number, tr.price_per_kwh
            FROM tariff.tariff_rate tr
            JOIN tariff.grid_tariff gt ON gt.id = tr.grid_tariff_id
            WHERE gt.grid_area_code = @GridAreaCode
              AND gt.tariff_type = @TariffType
              AND gt.valid_from <= @Date
              AND (gt.valid_to IS NULL OR gt.valid_to > @Date)
            ORDER BY tr.hour_number
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = await conn.QueryAsync<TariffRateRow>(
            new CommandDefinition(sql, new { GridAreaCode = gridAreaCode, TariffType = tariffType, Date = date }, cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<decimal> GetSubscriptionAsync(
        string gridAreaCode, string subscriptionType, DateOnly date, CancellationToken ct)
    {
        const string sql = """
            SELECT amount_kr_per_month
            FROM tariff.subscription
            WHERE grid_area_code = @GridAreaCode
              AND subscription_type = @SubscriptionType
              AND valid_from <= @Date
              AND (valid_to IS NULL OR valid_to > @Date)
            ORDER BY valid_from DESC
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        return await conn.QuerySingleAsync<decimal>(
            new CommandDefinition(sql, new { GridAreaCode = gridAreaCode, SubscriptionType = subscriptionType, Date = date }, cancellationToken: ct));
    }

    public async Task<decimal> GetElectricityTaxAsync(DateOnly date, CancellationToken ct)
    {
        const string sql = """
            SELECT rate_per_kwh
            FROM tariff.electricity_tax
            WHERE valid_from <= @Date
              AND (valid_to IS NULL OR valid_to > @Date)
            ORDER BY valid_from DESC
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        return await conn.QuerySingleAsync<decimal>(
            new CommandDefinition(sql, new { Date = date }, cancellationToken: ct));
    }

    public async Task SeedGridTariffAsync(
        string gridAreaCode, string tariffType, DateOnly validFrom,
        IReadOnlyList<TariffRateRow> rates, CancellationToken ct)
    {
        const string insertTariff = """
            INSERT INTO tariff.grid_tariff (grid_area_code, charge_owner_id, tariff_type, valid_from)
            VALUES (@GridAreaCode, 'seed', @TariffType, @ValidFrom)
            ON CONFLICT (grid_area_code, tariff_type, valid_from) DO UPDATE SET charge_owner_id = 'seed'
            RETURNING id
            """;

        const string insertRate = """
            INSERT INTO tariff.tariff_rate (grid_tariff_id, hour_number, price_per_kwh)
            VALUES (@GridTariffId, @HourNumber, @PricePerKwh)
            ON CONFLICT (grid_tariff_id, hour_number) DO UPDATE SET price_per_kwh = EXCLUDED.price_per_kwh
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var tariffId = await conn.QuerySingleAsync<Guid>(
            new CommandDefinition(insertTariff,
                new { GridAreaCode = gridAreaCode, TariffType = tariffType, ValidFrom = validFrom },
                cancellationToken: ct));

        var rateParams = rates.Select(r => new { GridTariffId = tariffId, r.HourNumber, r.PricePerKwh });
        await conn.ExecuteAsync(new CommandDefinition(insertRate, rateParams, cancellationToken: ct));
    }

    public async Task SeedSubscriptionAsync(
        string gridAreaCode, string subscriptionType, decimal amountPerMonth,
        DateOnly validFrom, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO tariff.subscription (grid_area_code, subscription_type, amount_kr_per_month, valid_from)
            VALUES (@GridAreaCode, @SubscriptionType, @AmountPerMonth, @ValidFrom)
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { GridAreaCode = gridAreaCode, SubscriptionType = subscriptionType, AmountPerMonth = amountPerMonth, ValidFrom = validFrom },
            cancellationToken: ct));
    }

    public async Task SeedElectricityTaxAsync(decimal ratePerKwh, DateOnly validFrom, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO tariff.electricity_tax (rate_per_kwh, valid_from)
            VALUES (@RatePerKwh, @ValidFrom)
            ON CONFLICT (valid_from) DO UPDATE SET rate_per_kwh = EXCLUDED.rate_per_kwh
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { RatePerKwh = ratePerKwh, ValidFrom = validFrom },
            cancellationToken: ct));
    }
}
