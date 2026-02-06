using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Infrastructure.Portfolio;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class PortfolioRepositoryTests
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

        var customer = await _sut.CreateCustomerAsync("Test Customer", "0101901234", "private", CancellationToken.None);
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
}
