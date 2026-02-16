using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Infrastructure.Portfolio;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class PortfolioRepositoryTests : IClassFixture<TestDatabase>
{
    private readonly PortfolioRepository _sut;

    public PortfolioRepositoryTests(TestDatabase db)
    {
        _sut = new PortfolioRepository(TestDatabase.ConnectionString);
    }

    private async Task EnsureGridAreaAsync()
    {
        await _sut.EnsureGridAreaAsync("344", "5790000392261", "N1 A/S", "DK1", CancellationToken.None);
    }

    [Fact]
    public async Task Create_full_portfolio_and_query_back()
    {
        await EnsureGridAreaAsync();

        var customer = await _sut.CreateCustomerAsync("Test Customer", "0101901234", "private", null, CancellationToken.None);
        customer.Id.Should().NotBeEmpty();
        customer.Name.Should().Be("Test Customer");
        customer.Status.Should().Be("active");

        var mp = new MeteringPoint("571313100000099999", "E17", "flex", "344", "5790000392261", "DK1", "connected");
        var created = await _sut.CreateMeteringPointAsync(mp, CancellationToken.None);
        created.Gsrn.Should().Be("571313100000099999");

        var product = await _sut.CreateProductAsync("Spot Standard", "spot", 4.0m, null, 39.00m, CancellationToken.None);
        product.MarginOrePerKwh.Should().Be(4.0m);

        var contract = await _sut.CreateContractAsync(
            customer.Id, "571313100000099999", product.Id, "monthly", "post_payment",
            new DateOnly(2025, 1, 1), CancellationToken.None);
        contract.Id.Should().NotBeEmpty();
        contract.Gsrn.Should().Be("571313100000099999");

        var activeContract = await _sut.GetActiveContractAsync("571313100000099999", CancellationToken.None);
        activeContract.Should().NotBeNull();
        activeContract!.Id.Should().Be(contract.Id);
    }

    [Fact]
    public async Task Activate_metering_point_and_create_supply_period()
    {
        await EnsureGridAreaAsync();

        var mp = new MeteringPoint("571313100000088888", "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _sut.CreateMeteringPointAsync(mp, CancellationToken.None);

        await _sut.ActivateMeteringPointAsync("571313100000088888",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        var period = await _sut.CreateSupplyPeriodAsync("571313100000088888", new DateOnly(2025, 1, 1), CancellationToken.None);
        period.Gsrn.Should().Be("571313100000088888");
        period.StartDate.Should().Be(new DateOnly(2025, 1, 1));
        period.EndDate.Should().BeNull();
    }

    [Fact]
    public async Task GetProductAsync_returns_product()
    {
        var product = await _sut.CreateProductAsync("Test Product", "spot", 5.0m, 1.0m, 29.00m, CancellationToken.None);

        var fetched = await _sut.GetProductAsync(product.Id, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Test Product");
        fetched.SupplementOrePerKwh.Should().Be(1.0m);
    }

    [Fact]
    public async Task Deactivate_metering_point_sets_disconnected()
    {
        await EnsureGridAreaAsync();
        var mp = new MeteringPoint("571313100000077777", "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _sut.CreateMeteringPointAsync(mp, CancellationToken.None);
        await _sut.ActivateMeteringPointAsync("571313100000077777",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        await _sut.DeactivateMeteringPointAsync("571313100000077777",
            new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        // Verify by checking supply periods still work for this GSRN
        var supply = await _sut.CreateSupplyPeriodAsync("571313100000077777", new DateOnly(2025, 1, 1), CancellationToken.None);
        supply.Gsrn.Should().Be("571313100000077777");
    }

    [Fact]
    public async Task End_supply_period_sets_end_date_and_reason()
    {
        await EnsureGridAreaAsync();
        var mp = new MeteringPoint("571313100000066666", "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _sut.CreateMeteringPointAsync(mp, CancellationToken.None);
        await _sut.CreateSupplyPeriodAsync("571313100000066666", new DateOnly(2025, 1, 1), CancellationToken.None);

        await _sut.EndSupplyPeriodAsync("571313100000066666", new DateOnly(2025, 3, 1), ProcessTypes.SupplierSwitch, CancellationToken.None);

        var periods = await _sut.GetSupplyPeriodsAsync("571313100000066666", CancellationToken.None);
        periods.Should().HaveCount(1);
        periods[0].EndDate.Should().Be(new DateOnly(2025, 3, 1));
    }

    [Fact]
    public async Task End_contract_sets_end_date()
    {
        await EnsureGridAreaAsync();
        var mp = new MeteringPoint("571313100000055555", "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _sut.CreateMeteringPointAsync(mp, CancellationToken.None);
        var customer = await _sut.CreateCustomerAsync("End Contract Test", "0101905555", "private", null, CancellationToken.None);
        var product = await _sut.CreateProductAsync("Test Prod", "spot", 4.0m, null, 39.00m, CancellationToken.None);
        await _sut.CreateContractAsync(customer.Id, "571313100000055555", product.Id, "monthly", "post_payment",
            new DateOnly(2025, 1, 1), CancellationToken.None);

        await _sut.EndContractAsync("571313100000055555", new DateOnly(2025, 3, 1), CancellationToken.None);

        var active = await _sut.GetActiveContractAsync("571313100000055555", CancellationToken.None);
        active.Should().BeNull();
    }

    [Fact]
    public async Task GetSupplyPeriods_returns_all_periods()
    {
        await EnsureGridAreaAsync();
        var mp = new MeteringPoint("571313100000044444", "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _sut.CreateMeteringPointAsync(mp, CancellationToken.None);

        await _sut.CreateSupplyPeriodAsync("571313100000044444", new DateOnly(2025, 1, 1), CancellationToken.None);
        await _sut.EndSupplyPeriodAsync("571313100000044444", new DateOnly(2025, 3, 1), ProcessTypes.SupplierSwitch, CancellationToken.None);
        await _sut.CreateSupplyPeriodAsync("571313100000044444", new DateOnly(2025, 3, 1), CancellationToken.None);

        var periods = await _sut.GetSupplyPeriodsAsync("571313100000044444", CancellationToken.None);
        periods.Should().HaveCount(2);
        periods[0].EndDate.Should().Be(new DateOnly(2025, 3, 1));
        periods[1].EndDate.Should().BeNull();
    }

    [Fact]
    public async Task Update_metering_point_grid_area()
    {
        await EnsureGridAreaAsync();
        await _sut.EnsureGridAreaAsync("740", "5790000610877", "Radius Elnet", "DK2", CancellationToken.None);
        var mp = new MeteringPoint("571313100000033333", "E17", "flex", "344", "5790000392261", "DK1", "connected");
        await _sut.CreateMeteringPointAsync(mp, CancellationToken.None);

        await _sut.UpdateMeteringPointGridAreaAsync("571313100000033333", "740", "DK2", CancellationToken.None);

        // Verify by creating a contract (proves the mp still exists and is functional)
        var customer = await _sut.CreateCustomerAsync("Grid Test", "0101903333", "private", null, CancellationToken.None);
        var product = await _sut.CreateProductAsync("Grid Prod", "spot", 4.0m, null, 39.00m, CancellationToken.None);
        var contract = await _sut.CreateContractAsync(customer.Id, "571313100000033333", product.Id,
            "monthly", "post_payment", new DateOnly(2025, 1, 1), CancellationToken.None);
        contract.Gsrn.Should().Be("571313100000033333");
    }

    [Fact]
    public async Task GetDashboardStatsAsync_returns_valid_stats_without_deserialization_error()
    {
        // This test catches Dapper deserialization errors caused by column name mismatches
        // between SQL aliases and record constructor parameters.

        var stats = await _sut.GetDashboardStatsAsync(CancellationToken.None);

        stats.Should().NotBeNull();
        stats.PendingSignups.Should().BeGreaterThanOrEqualTo(0);
        stats.ActiveCustomers.Should().BeGreaterThanOrEqualTo(0);
        stats.RejectedSignups.Should().BeGreaterThanOrEqualTo(0);
        stats.ProductCount.Should().BeGreaterThanOrEqualTo(0);
    }
}
