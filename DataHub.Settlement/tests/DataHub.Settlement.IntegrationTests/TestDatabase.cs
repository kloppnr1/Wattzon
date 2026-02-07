using DataHub.Settlement.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

public sealed class TestDatabase : IAsyncLifetime
{
    public const string ConnectionString =
        "Host=localhost;Port=5432;Database=datahub_settlement_test;Username=settlement;Password=settlement";

    private static bool _migrated;
    private static readonly object Lock = new();

    public async Task InitializeAsync()
    {
        lock (Lock)
        {
            if (!_migrated)
            {
                using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
                DatabaseMigrator.Migrate(ConnectionString, loggerFactory.CreateLogger("TestDb"));
                _migrated = true;
            }
        }

        await TruncateTablesAsync();
    }

    public async Task DisposeAsync()
    {
        await TruncateTablesAsync();
    }

    public NpgsqlConnection CreateConnection() => new(ConnectionString);

    private static async Task TruncateTablesAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            TRUNCATE
                billing.aconto_payment,
                settlement.erroneous_switch_reversal,
                settlement.correction_settlement,
                settlement.settlement_line,
                settlement.settlement_run,
                settlement.billing_period,
                lifecycle.process_event,
                lifecycle.process_request,
                datahub.dead_letter,
                datahub.processed_message_id,
                datahub.inbound_message,
                datahub.outbound_request,
                tariff.tariff_rate,
                tariff.grid_tariff,
                tariff.subscription,
                tariff.electricity_tax,
                portfolio.contract,
                portfolio.supply_period,
                portfolio.metering_point,
                portfolio.product,
                portfolio.customer,
                metering.annual_consumption_tracker,
                metering.metering_data_history,
                metering.metering_data,
                metering.spot_price
            CASCADE;
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
