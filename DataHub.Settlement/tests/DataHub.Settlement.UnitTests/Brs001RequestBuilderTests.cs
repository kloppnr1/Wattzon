using System.Text.Json;
using DataHub.Settlement.Infrastructure.DataHub;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class Brs001RequestBuilderTests
{
    private readonly Brs001RequestBuilder _sut = new();

    [Fact]
    public void BuildBrs001_produces_valid_json_with_correct_gsrn()
    {
        var json = _sut.BuildBrs001("571313100000012345", "0101901234", new DateOnly(2025, 2, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestChangeOfSupplier_MarketDocument");

        root.GetProperty("process").GetProperty("processType").GetProperty("value").GetString()
            .Should().Be("E65");

        var activity = root.GetProperty("MktActivityRecord");
        activity.GetProperty("marketEvaluationPoint").GetProperty("mRID").GetProperty("value").GetString()
            .Should().Be("571313100000012345");

        activity.GetProperty("customer_MarketParticipant").GetProperty("mRID").GetProperty("value").GetString()
            .Should().Be("0101901234");
    }

    [Fact]
    public void BuildBrs001_contains_sender_and_receiver()
    {
        var json = _sut.BuildBrs001("571313100000012345", "0101901234", new DateOnly(2025, 2, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestChangeOfSupplier_MarketDocument");

        root.GetProperty("sender_MarketParticipant").GetProperty("mRID").GetProperty("value").GetString()
            .Should().NotBeNullOrEmpty();

        root.GetProperty("receiver_MarketParticipant").GetProperty("mRID").GetProperty("value").GetString()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BuildBrs001_effective_date_is_correct()
    {
        var json = _sut.BuildBrs001("571313100000012345", "0101901234", new DateOnly(2025, 2, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestChangeOfSupplier_MarketDocument");

        var dateStr = root.GetProperty("MktActivityRecord")
            .GetProperty("start_DateAndOrTime")
            .GetProperty("dateTime").GetString();

        dateStr.Should().StartWith("2025-02-01");
    }
}
