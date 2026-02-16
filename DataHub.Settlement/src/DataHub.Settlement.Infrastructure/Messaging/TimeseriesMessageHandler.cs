using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Infrastructure.Settlement;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class TimeseriesMessageHandler : IMessageHandler
{
    private readonly ICimParser _parser;
    private readonly IMeteringDataRepository _meteringRepo;
    private readonly SettlementTriggerService? _settlementTrigger;
    private readonly ILogger<TimeseriesMessageHandler> _logger;

    public TimeseriesMessageHandler(
        ICimParser parser,
        IMeteringDataRepository meteringRepo,
        ILogger<TimeseriesMessageHandler> logger,
        SettlementTriggerService? settlementTrigger = null)
    {
        _parser = parser;
        _meteringRepo = meteringRepo;
        _logger = logger;
        _settlementTrigger = settlementTrigger;
    }

    public QueueName Queue => QueueName.Timeseries;

    public async Task HandleAsync(DataHubMessage message, CancellationToken ct)
    {
        var seriesList = _parser.ParseRsm012(message.RawPayload);
        var processedGsrns = new HashSet<string>();

        foreach (var series in seriesList)
        {
            var regTimestamp = series.RegistrationTimestamp.UtcDateTime;

            var validPoints = new List<MeteringDataRow>();
            foreach (var p in series.Points)
            {
                if (p.QuantityKwh < 0)
                {
                    _logger.LogWarning(
                        "Skipping negative quantity {Quantity} kWh at position {Position} for {Gsrn}",
                        p.QuantityKwh, p.Position, series.MeteringPointId);
                    continue;
                }

                validPoints.Add(new MeteringDataRow(
                    p.Timestamp.UtcDateTime, series.Resolution, p.QuantityKwh, p.QualityCode,
                    series.TransactionId, regTimestamp));
            }

            if (validPoints.Count == 0)
                continue;

            var changedCount = await _meteringRepo.StoreTimeSeriesWithHistoryAsync(series.MeteringPointId, validPoints, ct);
            processedGsrns.Add(series.MeteringPointId);

            if (changedCount > 0)
            {
                _logger.LogInformation(
                    "Detected {ChangedCount} corrected readings for {Gsrn} — correction settlement may be needed",
                    changedCount, series.MeteringPointId);
            }
        }

        if (_settlementTrigger is not null)
        {
            foreach (var gsrn in processedGsrns)
            {
                try
                {
                    await _settlementTrigger.TrySettleAsync(gsrn, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "RSM-012 triggered settlement failed for GSRN {Gsrn}", gsrn);
                }
            }
        }
    }
}
