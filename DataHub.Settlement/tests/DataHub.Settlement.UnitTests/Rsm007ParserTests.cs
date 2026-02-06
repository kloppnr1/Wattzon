using DataHub.Settlement.Infrastructure.Parsing;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class Rsm007ParserTests
{
    private readonly CimJsonParser _sut = new();

    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine("..", "..", "..", "..", "..", "fixtures", "rsm007-activation.json"));

    [Fact]
    public void ParseRsm007_extracts_gsrn()
    {
        var result = _sut.ParseRsm007(LoadFixture());

        result.MeteringPointId.Should().Be("571313100000012345");
    }

    [Fact]
    public void ParseRsm007_extracts_grid_area_and_price_area()
    {
        var result = _sut.ParseRsm007(LoadFixture());

        result.GridAreaCode.Should().Be("344");
        result.PriceArea.Should().Be("DK1");
    }

    [Fact]
    public void ParseRsm007_extracts_type_and_settlement_method()
    {
        var result = _sut.ParseRsm007(LoadFixture());

        result.Type.Should().Be("E17");
        result.SettlementMethod.Should().Be("flex");
    }

    [Fact]
    public void ParseRsm007_extracts_supply_start()
    {
        var result = _sut.ParseRsm007(LoadFixture());

        result.SupplyStart.Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ParseRsm007_extracts_message_id_and_grid_operator()
    {
        var result = _sut.ParseRsm007(LoadFixture());

        result.MessageId.Should().Be("msg-rsm007-001");
        result.GridOperatorGln.Should().Be("5790000392261");
    }
}
