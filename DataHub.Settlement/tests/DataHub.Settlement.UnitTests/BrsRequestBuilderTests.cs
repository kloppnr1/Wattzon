using System.Text.Json;
using DataHub.Settlement.Infrastructure.DataHub;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class BrsRequestBuilderTests
{
    private readonly BrsRequestBuilder _sut = new();

    [Fact]
    public void BuildBrs001_produces_valid_json_with_correct_gsrn()
    {
        var json = _sut.BuildBrs001("571313100000012345", "0101901234", new DateOnly(2025, 2, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestChangeOfSupplier_MarketDocument");

        root.GetProperty("process").GetProperty("processType").GetProperty("value").GetString()
            .Should().Be("E03");

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

    // ── BRS-002: End of Supply ──

    [Fact]
    public void BuildBrs002_produces_end_of_supply_with_E20_process_type()
    {
        var json = _sut.BuildBrs002("571313100000012345", new DateOnly(2025, 3, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestEndOfSupply_MarketDocument");

        root.GetProperty("process").GetProperty("processType").GetProperty("value").GetString()
            .Should().Be("E20");

        root.GetProperty("MktActivityRecord")
            .GetProperty("marketEvaluationPoint").GetProperty("mRID").GetProperty("value").GetString()
            .Should().Be("571313100000012345");
    }

    [Fact]
    public void BuildBrs002_contains_end_date()
    {
        var json = _sut.BuildBrs002("571313100000012345", new DateOnly(2025, 3, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestEndOfSupply_MarketDocument");

        var dateStr = root.GetProperty("MktActivityRecord")
            .GetProperty("end_DateAndOrTime")
            .GetProperty("dateTime").GetString();

        dateStr.Should().StartWith("2025-03-01");
    }

    // ── BRS-003: Cancel Change of Supplier ──

    [Fact]
    public void BuildBrs003_produces_cancel_with_E03_process_type()
    {
        var json = _sut.BuildBrs003("571313100000012345", "corr-original-001");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestCancelChangeOfSupplier_MarketDocument");

        root.GetProperty("process").GetProperty("processType").GetProperty("value").GetString()
            .Should().Be("E03");

        root.GetProperty("MktActivityRecord")
            .GetProperty("originalTransactionID").GetString()
            .Should().Be("corr-original-001");
    }

    [Fact]
    public void BuildBrs003_contains_gsrn()
    {
        var json = _sut.BuildBrs003("571313100000012345", "corr-original-001");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestCancelChangeOfSupplier_MarketDocument");

        root.GetProperty("MktActivityRecord")
            .GetProperty("marketEvaluationPoint").GetProperty("mRID").GetProperty("value").GetString()
            .Should().Be("571313100000012345");
    }

    // ── BRS-009: Change of Supplier (move-in) ──

    [Fact]
    public void BuildBrs009_uses_E65_process_type()
    {
        var json = _sut.BuildBrs009("571313100000012345", "0101901234", new DateOnly(2025, 2, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestChangeOfSupplier_MarketDocument");

        root.GetProperty("process").GetProperty("processType").GetProperty("value").GetString()
            .Should().Be("E65");
    }

    [Fact]
    public void BuildBrs009_contains_customer_and_gsrn()
    {
        var json = _sut.BuildBrs009("571313100000012345", "0101901234", new DateOnly(2025, 2, 1));

        using var doc = JsonDocument.Parse(json);
        var activity = doc.RootElement.GetProperty("RequestChangeOfSupplier_MarketDocument")
            .GetProperty("MktActivityRecord");

        activity.GetProperty("marketEvaluationPoint").GetProperty("mRID").GetProperty("value").GetString()
            .Should().Be("571313100000012345");
        activity.GetProperty("customer_MarketParticipant").GetProperty("mRID").GetProperty("value").GetString()
            .Should().Be("0101901234");
    }

    // ── BRS-010: End of Supply (move-out) ──

    [Fact]
    public void BuildBrs010_uses_E66_process_type()
    {
        var json = _sut.BuildBrs010("571313100000012345", new DateOnly(2025, 4, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestEndOfSupply_MarketDocument");

        root.GetProperty("process").GetProperty("processType").GetProperty("value").GetString()
            .Should().Be("E66");
    }

    [Fact]
    public void BuildBrs010_contains_end_date()
    {
        var json = _sut.BuildBrs010("571313100000012345", new DateOnly(2025, 4, 1));

        using var doc = JsonDocument.Parse(json);
        var dateStr = doc.RootElement.GetProperty("RequestEndOfSupply_MarketDocument")
            .GetProperty("MktActivityRecord")
            .GetProperty("end_DateAndOrTime")
            .GetProperty("dateTime").GetString();

        dateStr.Should().StartWith("2025-04-01");
    }

    // ── BRS-043: Short-notice change of supplier ──

    [Fact]
    public void BuildBrs043_uses_E03_process_type()
    {
        var json = _sut.BuildBrs043("571313100000012345", "0101901234", new DateOnly(2025, 2, 1));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestChangeOfSupplier_MarketDocument");

        root.GetProperty("process").GetProperty("processType").GetProperty("value").GetString()
            .Should().Be("E03");
    }

    [Fact]
    public void BuildBrs043_contains_customer()
    {
        var json = _sut.BuildBrs043("571313100000012345", "0101901234", new DateOnly(2025, 2, 1));

        using var doc = JsonDocument.Parse(json);
        var activity = doc.RootElement.GetProperty("RequestChangeOfSupplier_MarketDocument")
            .GetProperty("MktActivityRecord");

        activity.GetProperty("customer_MarketParticipant").GetProperty("mRID").GetProperty("value").GetString()
            .Should().Be("0101901234");
    }

    // ── BRS-044: Cancel end of supply ──

    [Fact]
    public void BuildBrs044_uses_E20_process_type()
    {
        var json = _sut.BuildBrs044("571313100000012345", "corr-eos-001");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestCancelChangeOfSupplier_MarketDocument");

        root.GetProperty("process").GetProperty("processType").GetProperty("value").GetString()
            .Should().Be("E20");
    }

    [Fact]
    public void BuildBrs044_contains_original_correlation()
    {
        var json = _sut.BuildBrs044("571313100000012345", "corr-eos-001");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("RequestCancelChangeOfSupplier_MarketDocument");

        root.GetProperty("MktActivityRecord")
            .GetProperty("originalTransactionID").GetString()
            .Should().Be("corr-eos-001");
    }
}
