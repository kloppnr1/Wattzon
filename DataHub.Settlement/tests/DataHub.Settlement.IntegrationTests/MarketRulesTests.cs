using Dapper;
using DataHub.Settlement.Infrastructure.Dashboard;
using DataHub.Settlement.Infrastructure.Database;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public sealed class MarketRulesTests
{
    private static readonly string Conn = TestDatabase.ConnectionString;
    private static readonly CancellationToken Ct = CancellationToken.None;

    static MarketRulesTests()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    // ── CanChangeSupplier ────────────────────────────────────────────

    [Fact]
    public async Task CanChangeSupplier_empty_database_returns_valid()
    {
        var result = await MarketRules.CanChangeSupplierAsync("571313100000070001", Conn, Ct);

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CanChangeSupplier_with_active_supply_returns_invalid()
    {
        const string gsrn = "571313100000070002";
        await SeedMeteringPoint(gsrn);
        await SeedSupplyPeriod(gsrn, endDate: null);

        var result = await MarketRules.CanChangeSupplierAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Already supplying");
    }

    [Fact]
    public async Task CanChangeSupplier_with_ended_supply_returns_valid()
    {
        const string gsrn = "571313100000070003";
        await SeedMeteringPoint(gsrn);
        await SeedSupplyPeriod(gsrn, endDate: new DateOnly(2025, 2, 1));

        var result = await MarketRules.CanChangeSupplierAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CanChangeSupplier_with_pending_process_returns_invalid()
    {
        const string gsrn = "571313100000070004";
        await SeedProcessRequest(gsrn, "sent_to_datahub");

        var result = await MarketRules.CanChangeSupplierAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Conflicting process");
    }

    [Fact]
    public async Task CanChangeSupplier_with_completed_process_and_no_supply_returns_valid()
    {
        const string gsrn = "571313100000070005";
        await SeedProcessRequest(gsrn, "completed");

        var result = await MarketRules.CanChangeSupplierAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeTrue();
    }

    // ── CanReceiveMetering ───────────────────────────────────────────

    [Fact]
    public async Task CanReceiveMetering_no_supply_returns_invalid()
    {
        var result = await MarketRules.CanReceiveMeteringAsync("571313100000071001", Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active supply period");
    }

    [Fact]
    public async Task CanReceiveMetering_supply_exists_but_not_activated_returns_invalid()
    {
        const string gsrn = "571313100000071002";
        await SeedMeteringPoint(gsrn, activated: false);
        await SeedSupplyPeriod(gsrn, endDate: null);

        var result = await MarketRules.CanReceiveMeteringAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not activated");
    }

    [Fact]
    public async Task CanReceiveMetering_active_supply_and_activated_returns_valid()
    {
        const string gsrn = "571313100000071003";
        await SeedMeteringPoint(gsrn, activated: true);
        await SeedSupplyPeriod(gsrn, endDate: null);

        var result = await MarketRules.CanReceiveMeteringAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeTrue();
    }

    // ── CanRunSettlement ─────────────────────────────────────────────

    [Fact]
    public async Task CanRunSettlement_no_contract_returns_invalid()
    {
        var result = await MarketRules.CanRunSettlementAsync("571313100000072001", Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active contract");
    }

    [Fact]
    public async Task CanRunSettlement_contract_exists_but_no_metering_data_returns_invalid()
    {
        const string gsrn = "571313100000072002";
        await SeedMeteringPoint(gsrn);
        await SeedContract(gsrn, endDate: null);

        var result = await MarketRules.CanRunSettlementAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No unsettled metering data");
    }

    [Fact]
    public async Task CanRunSettlement_contract_and_metering_data_returns_valid()
    {
        const string gsrn = "571313100000072003";
        await SeedMeteringPoint(gsrn);
        await SeedContract(gsrn, endDate: null);
        await SeedMeteringData(gsrn);

        var result = await MarketRules.CanRunSettlementAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeTrue();
    }

    // ── CanOffboard ──────────────────────────────────────────────────

    [Fact]
    public async Task CanOffboard_no_supply_returns_invalid()
    {
        var result = await MarketRules.CanOffboardAsync("571313100000073001", Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active supply period");
    }

    [Fact]
    public async Task CanOffboard_supply_exists_but_no_completed_process_returns_invalid()
    {
        const string gsrn = "571313100000073002";
        await SeedMeteringPoint(gsrn);
        await SeedSupplyPeriod(gsrn, endDate: null);
        await SeedProcessRequest(gsrn, "sent_to_datahub");

        var result = await MarketRules.CanOffboardAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No completed process");
    }

    [Fact]
    public async Task CanOffboard_supply_and_completed_process_returns_valid()
    {
        const string gsrn = "571313100000073003";
        await SeedMeteringPoint(gsrn);
        await SeedSupplyPeriod(gsrn, endDate: null);
        await SeedProcessRequest(gsrn, "completed");

        var result = await MarketRules.CanOffboardAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeTrue();
    }

    // ── CanBillAconto ────────────────────────────────────────────────

    [Fact]
    public async Task CanBillAconto_no_supply_returns_invalid()
    {
        var result = await MarketRules.CanBillAcontoAsync("571313100000074001", Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active supply period");
    }

    [Fact]
    public async Task CanBillAconto_supply_exists_but_no_contract_returns_invalid()
    {
        const string gsrn = "571313100000074002";
        await SeedMeteringPoint(gsrn);
        await SeedSupplyPeriod(gsrn, endDate: null);

        var result = await MarketRules.CanBillAcontoAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active contract");
    }

    [Fact]
    public async Task CanBillAconto_supply_and_contract_returns_valid()
    {
        const string gsrn = "571313100000074003";
        await SeedMeteringPoint(gsrn);
        await SeedSupplyPeriod(gsrn, endDate: null);
        await SeedContract(gsrn, endDate: null);

        var result = await MarketRules.CanBillAcontoAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeTrue();
    }

    // ── Post-offboard: all operations rejected ───────────────────────

    [Fact]
    public async Task After_offboard_all_follow_up_operations_are_rejected()
    {
        const string gsrn = "571313100000075001";
        await SeedMeteringPoint(gsrn);
        await SeedSupplyPeriod(gsrn, endDate: new DateOnly(2025, 2, 16));
        await SeedContract(gsrn, endDate: new DateOnly(2025, 2, 16));
        await SeedProcessRequest(gsrn, "final_settled");

        var meteringResult = await MarketRules.CanReceiveMeteringAsync(gsrn, Conn, Ct);
        var settlementResult = await MarketRules.CanRunSettlementAsync(gsrn, Conn, Ct);
        var offboardResult = await MarketRules.CanOffboardAsync(gsrn, Conn, Ct);
        var acontoResult = await MarketRules.CanBillAcontoAsync(gsrn, Conn, Ct);

        meteringResult.IsValid.Should().BeFalse("supply ended");
        settlementResult.IsValid.Should().BeFalse("contract ended");
        offboardResult.IsValid.Should().BeFalse("supply ended");
        acontoResult.IsValid.Should().BeFalse("supply ended");
    }

    [Fact]
    public async Task After_offboard_re_onboarding_is_allowed()
    {
        const string gsrn = "571313100000075002";
        await SeedMeteringPoint(gsrn);
        await SeedSupplyPeriod(gsrn, endDate: new DateOnly(2025, 2, 16));
        await SeedProcessRequest(gsrn, "final_settled");

        var result = await MarketRules.CanChangeSupplierAsync(gsrn, Conn, Ct);

        result.IsValid.Should().BeTrue("ended supply allows re-onboarding");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static async Task EnsureGridArea()
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO portfolio.grid_area (code, grid_operator_gln, grid_operator_name, price_area)
            VALUES ('344', '5790000392261', 'N1 A/S', 'DK1')
            ON CONFLICT (code) DO NOTHING
            """);
    }

    private static async Task SeedMeteringPoint(string gsrn, bool activated = false)
    {
        await EnsureGridArea();
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, grid_area_code, grid_operator_gln, price_area, activated_at)
            VALUES (@Gsrn, 'E17', 'flex', '344', '5790000392261', 'DK1', @ActivatedAt)
            ON CONFLICT (gsrn) DO NOTHING
            """, new
        {
            Gsrn = gsrn,
            ActivatedAt = activated ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : (DateTime?)null,
        });
    }

    private static async Task SeedSupplyPeriod(string gsrn, DateOnly? endDate)
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO portfolio.supply_period (gsrn, start_date, end_date)
            VALUES (@Gsrn, @StartDate, @EndDate)
            """, new { Gsrn = gsrn, StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), EndDate = endDate?.ToDateTime(TimeOnly.MinValue) });
    }

    private static async Task SeedContract(string gsrn, DateOnly? endDate)
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();

        var customerId = await conn.QuerySingleAsync<Guid>("""
            INSERT INTO portfolio.customer (name, cpr_cvr, contact_type)
            VALUES (@Name, '0101901234', 'private')
            RETURNING id
            """, new { Name = $"Test-{gsrn[^4..]}" });

        var productId = await conn.QuerySingleAsync<Guid>("""
            INSERT INTO portfolio.product (name, energy_model, margin_ore_per_kwh, subscription_kr_per_month)
            VALUES (@Name, 'spot', 4.0, 39.00)
            RETURNING id
            """, new { Name = $"Spot-{gsrn[^4..]}" });

        await conn.ExecuteAsync("""
            INSERT INTO portfolio.contract (customer_id, gsrn, product_id, billing_frequency, payment_model, start_date, end_date)
            VALUES (@CustomerId, @Gsrn, @ProductId, 'monthly', 'post_payment', @StartDate, @EndDate)
            """, new
        {
            CustomerId = customerId,
            Gsrn = gsrn,
            ProductId = productId,
            StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = endDate?.ToDateTime(TimeOnly.MinValue),
        });
    }

    private static async Task SeedProcessRequest(string gsrn, string status)
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO lifecycle.process_request (gsrn, process_type, status, effective_date)
            VALUES (@Gsrn, 'supplier_switch', @Status, @EffectiveDate)
            """, new
        {
            Gsrn = gsrn,
            Status = status,
            EffectiveDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }

    private static async Task SeedMeteringData(string gsrn)
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        for (var i = 0; i < 24; i++)
        {
            await conn.ExecuteAsync("""
                INSERT INTO metering.metering_data (metering_point_id, timestamp, resolution, quantity_kwh, quality_code, source_message_id)
                VALUES (@Gsrn, @Ts, 'PT1H', 0.55, 'A01', 'test-msg')
                """, new
            {
                Gsrn = gsrn,
                Ts = new DateTime(2025, 1, 1, i, 0, 0, DateTimeKind.Utc),
            });
        }
    }
}
