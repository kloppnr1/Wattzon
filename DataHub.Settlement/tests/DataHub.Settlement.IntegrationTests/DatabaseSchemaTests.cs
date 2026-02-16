using DataHub.Settlement.Infrastructure.Database;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// Integration tests that verify the database schema after migrations.
/// Requires TimescaleDB running via docker compose.
/// </summary>
[Collection("Database")]
public class DatabaseSchemaTests : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private NpgsqlConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData("portfolio", "grid_area")]
    [InlineData("portfolio", "customer")]
    [InlineData("portfolio", "metering_point")]
    [InlineData("portfolio", "product")]
    [InlineData("portfolio", "supply_period")]
    [InlineData("portfolio", "contract")]
    [InlineData("metering", "metering_data")]
    [InlineData("metering", "spot_price")]
    [InlineData("tariff", "grid_tariff")]
    [InlineData("tariff", "tariff_rate")]
    [InlineData("tariff", "subscription")]
    [InlineData("tariff", "electricity_tax")]
    [InlineData("settlement", "billing_period")]
    [InlineData("settlement", "settlement_run")]
    [InlineData("settlement", "settlement_line")]
    [InlineData("datahub", "inbound_message")]
    [InlineData("datahub", "processed_message_id")]
    [InlineData("datahub", "dead_letter")]
    [InlineData("datahub", "outbound_request")]
    [InlineData("lifecycle", "process_request")]
    [InlineData("lifecycle", "process_event")]
    [InlineData("metering", "metering_data_history")]
    [InlineData("metering", "annual_consumption_tracker")]
    [InlineData("settlement", "correction_settlement")]
    [InlineData("settlement", "erroneous_switch_reversal")]
    [InlineData("billing", "invoice")]
    [InlineData("billing", "invoice_line")]
    [InlineData("billing", "payment")]
    [InlineData("billing", "payment_allocation")]
    public async Task Table_Exists(string schema, string table)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table)",
            _connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);

        var exists = (bool)(await cmd.ExecuteScalarAsync())!;
        exists.Should().BeTrue($"table {schema}.{table} should exist");
    }

    [Fact]
    public async Task All_26_Tables_Exist()
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT COUNT(*) FROM information_schema.tables
              WHERE table_schema IN ('portfolio', 'metering', 'tariff', 'settlement', 'datahub', 'lifecycle', 'billing')
                AND table_type = 'BASE TABLE'",
            _connection);

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(34, "30 base tables + 4 invoice/payment tables (V004) requires exactly 34 tables");
    }

    [Fact]
    public async Task MeteringData_Is_Hypertable()
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT EXISTS (
                SELECT 1 FROM timescaledb_information.hypertables
                WHERE hypertable_schema = 'metering' AND hypertable_name = 'metering_data'
              )",
            _connection);

        var isHypertable = (bool)(await cmd.ExecuteScalarAsync())!;
        isHypertable.Should().BeTrue("metering_data should be a TimescaleDB hypertable");
    }

    [Fact]
    public async Task MeteringData_Has_Compression_Enabled()
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT compression_enabled FROM timescaledb_information.hypertables
              WHERE hypertable_schema = 'metering' AND hypertable_name = 'metering_data'",
            _connection);

        var compressionEnabled = (bool)(await cmd.ExecuteScalarAsync())!;
        compressionEnabled.Should().BeTrue("metering_data should have compression enabled");
    }

    [Fact]
    public async Task Can_Insert_And_Select_GridArea()
    {
        await using var tx = await _connection.BeginTransactionAsync();

        await using var insert = new NpgsqlCommand(
            @"INSERT INTO portfolio.grid_area (code, grid_operator_gln, grid_operator_name, price_area)
              VALUES ('999', '5790000000001', 'Test Grid Operator', 'DK1')
              ON CONFLICT (code) DO NOTHING",
            _connection, tx);
        await insert.ExecuteNonQueryAsync();

        await using var select = new NpgsqlCommand(
            "SELECT grid_operator_name FROM portfolio.grid_area WHERE code = '999'",
            _connection, tx);
        var name = (string)(await select.ExecuteScalarAsync())!;
        name.Should().Be("Test Grid Operator");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Can_Insert_And_Select_MeteringData()
    {
        await using var tx = await _connection.BeginTransactionAsync();

        await using var insert = new NpgsqlCommand(
            @"INSERT INTO metering.metering_data (metering_point_id, timestamp, resolution, quantity_kwh, quality_code, source_message_id)
              VALUES ('571313100000099999', '2025-01-15T10:00:00Z', 'PT15M', 0.375, 'A01', 'test-msg-001')
              ON CONFLICT DO NOTHING",
            _connection, tx);
        await insert.ExecuteNonQueryAsync();

        await using var select = new NpgsqlCommand(
            @"SELECT quantity_kwh FROM metering.metering_data
              WHERE metering_point_id = '571313100000099999' AND timestamp = '2025-01-15T10:00:00Z'",
            _connection, tx);
        var kwh = (decimal)(await select.ExecuteScalarAsync())!;
        kwh.Should().Be(0.375m);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Can_Insert_And_Select_SpotPrice()
    {
        await using var tx = await _connection.BeginTransactionAsync();

        await using var insert = new NpgsqlCommand(
            @"INSERT INTO metering.spot_price (price_area, ""timestamp"", price_per_kwh, resolution)
              VALUES ('DK1', '2025-01-15T10:00:00Z', 1.234567, 'PT1H')
              ON CONFLICT (price_area, ""timestamp"") DO UPDATE SET price_per_kwh = EXCLUDED.price_per_kwh",
            _connection, tx);
        await insert.ExecuteNonQueryAsync();

        await using var select = new NpgsqlCommand(
            @"SELECT price_per_kwh FROM metering.spot_price WHERE price_area = 'DK1' AND ""timestamp"" = '2025-01-15T10:00:00Z'",
            _connection, tx);
        var price = (decimal)(await select.ExecuteScalarAsync())!;
        price.Should().Be(1.234567m);

        await tx.RollbackAsync();
    }

    [Fact]
    public void Migrations_Are_Idempotent()
    {
        // Running migrations a second time should succeed without errors
        var logger = NullLoggerFactory.Instance.CreateLogger("TestMigrator");
        var act = () => DatabaseMigrator.Migrate(TestDatabase.ConnectionString, logger);
        act.Should().NotThrow("DbUp journal prevents re-execution of already-applied scripts");
    }
}
