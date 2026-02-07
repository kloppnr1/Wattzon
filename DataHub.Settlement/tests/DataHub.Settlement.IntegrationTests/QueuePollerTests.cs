using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Parsing;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.UnitTests;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Xunit;
using DataHub.Settlement.Application.DataHub;

namespace DataHub.Settlement.IntegrationTests;

[Collection("Database")]
public class QueuePollerTests
{
    private readonly MeteringDataRepository _meteringRepo;
    private readonly PortfolioRepository _portfolioRepo;
    private readonly MessageLog _messageLog;

    public QueuePollerTests(TestDatabase db)
    {
        _meteringRepo = new MeteringDataRepository(TestDatabase.ConnectionString);
        _portfolioRepo = new PortfolioRepository(TestDatabase.ConnectionString);
        _messageLog = new MessageLog(TestDatabase.ConnectionString);
    }

    private static string LoadSingleDayFixture() =>
        File.ReadAllText(Path.Combine("..", "..", "..", "..", "..", "fixtures", "rsm012-single-day.json"));

    [Fact]
    public async Task Processes_rsm012_message_end_to_end()
    {
        var client = new FakeDataHubClient();
        var parser = new CimJsonParser();
        var poller = new QueuePollerService(client, parser, _meteringRepo, _portfolioRepo, _messageLog,
            NullLogger<QueuePollerService>.Instance);

        client.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-001", "RSM-012", null, LoadSingleDayFixture()));

        var processed = await poller.PollQueueAsync(QueueName.Timeseries, CancellationToken.None);

        processed.Should().BeTrue();

        // Message should be dequeued
        var peek = await client.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        peek.Should().BeNull();

        // Data should be stored
        var rows = await _meteringRepo.GetConsumptionAsync("571313100000012345",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);
        rows.Should().HaveCount(24);
    }

    [Fact]
    public async Task Duplicate_message_is_skipped()
    {
        var client = new FakeDataHubClient();
        var parser = new CimJsonParser();
        var poller = new QueuePollerService(client, parser, _meteringRepo, _portfolioRepo, _messageLog,
            NullLogger<QueuePollerService>.Instance);

        client.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-dup", "RSM-012", null, LoadSingleDayFixture()));

        // Process first time
        await poller.PollQueueAsync(QueueName.Timeseries, CancellationToken.None);

        // Enqueue again with same message ID
        client.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-dup", "RSM-012", null, LoadSingleDayFixture()));

        // Process second time — should skip
        var processed = await poller.PollQueueAsync(QueueName.Timeseries, CancellationToken.None);
        processed.Should().BeTrue();

        // Message should still be dequeued (skip + dequeue)
        var peek = await client.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        peek.Should().BeNull();
    }

    [Fact]
    public async Task Malformed_message_is_dead_lettered()
    {
        var client = new FakeDataHubClient();
        var parser = new CimJsonParser();
        var poller = new QueuePollerService(client, parser, _meteringRepo, _portfolioRepo, _messageLog,
            NullLogger<QueuePollerService>.Instance);

        client.Enqueue(QueueName.Timeseries, new DataHubMessage("msg-bad", "RSM-012", null, "{ invalid json payload }"));

        var processed = await poller.PollQueueAsync(QueueName.Timeseries, CancellationToken.None);

        processed.Should().BeTrue();

        // Queue should be freed
        var peek = await client.PeekAsync(QueueName.Timeseries, CancellationToken.None);
        peek.Should().BeNull();
    }
}
