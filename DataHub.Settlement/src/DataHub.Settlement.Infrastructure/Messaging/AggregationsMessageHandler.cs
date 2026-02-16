using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Parsing;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class AggregationsMessageHandler : IMessageHandler
{
    private readonly ICimParser _parser;
    private readonly ILogger<AggregationsMessageHandler> _logger;

    public AggregationsMessageHandler(
        ICimParser parser,
        ILogger<AggregationsMessageHandler> logger)
    {
        _parser = parser;
        _logger = logger;
    }

    public QueueName Queue => QueueName.Aggregations;

    public Task HandleAsync(DataHubMessage message, CancellationToken ct)
    {
        var aggregation = _parser.ParseRsm014(message.RawPayload);

        _logger.LogInformation(
            "RSM-014: Received aggregation for grid area {GridArea}, period {Start}–{End}, total {TotalKwh} kWh, {PointCount} points",
            aggregation.GridAreaCode, aggregation.PeriodStart, aggregation.PeriodEnd,
            aggregation.TotalKwh, aggregation.Points.Count);

        return Task.CompletedTask;
    }
}
