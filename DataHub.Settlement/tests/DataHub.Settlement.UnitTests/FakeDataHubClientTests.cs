using DataHub.Settlement.Application.DataHub;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class FakeDataHubClientTests
{
    private readonly FakeDataHubClient _sut = new();

    [Fact]
    public async Task Peek_returns_null_on_empty_queue()
    {
        var result = await _sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Enqueue_then_Peek_returns_the_message()
    {
        var message = new DataHubMessage("msg-1", "RSM-012", null, "<payload/>");

        _sut.Enqueue(QueueName.Timeseries, message);

        var result = await _sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);

        result.Should().Be(message);
    }

    [Fact]
    public async Task Peek_does_not_remove_the_message()
    {
        var message = new DataHubMessage("msg-1", "RSM-012", null, "<payload/>");
        _sut.Enqueue(QueueName.Timeseries, message);

        var first = await _sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        var second = await _sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);

        first.Should().Be(message);
        second.Should().Be(message);
    }

    [Fact]
    public async Task Dequeue_removes_the_message()
    {
        var message = new DataHubMessage("msg-1", "RSM-012", null, "<payload/>");
        _sut.Enqueue(QueueName.Timeseries, message);

        await _sut.DequeueAsync("msg-1", CancellationToken.None);

        var result = await _sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SendRequestAsync_returns_accepted_response()
    {
        var response = await _sut.SendRequestAsync("BRS-001", "<cim/>", CancellationToken.None);

        response.Accepted.Should().BeTrue();
        response.RejectionReason.Should().BeNull();
        response.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Multiple_queues_are_independent()
    {
        var tsMessage = new DataHubMessage("ts-1", "RSM-012", null, "<ts/>");
        var mdMessage = new DataHubMessage("md-1", "RSM-014", null, "<md/>");

        _sut.Enqueue(QueueName.Timeseries, tsMessage);
        _sut.Enqueue(QueueName.MasterData, mdMessage);

        await _sut.DequeueAsync("ts-1", CancellationToken.None);

        var tsResult = await _sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        var mdResult = await _sut.PeekAsync(QueueName.MasterData, CancellationToken.None);

        tsResult.Should().BeNull();
        mdResult.Should().Be(mdMessage);
    }
}
