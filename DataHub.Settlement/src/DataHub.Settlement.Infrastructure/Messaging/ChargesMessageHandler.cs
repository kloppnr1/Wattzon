using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Infrastructure.Parsing;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public sealed class ChargesMessageHandler : IMessageHandler
{
    private readonly ICimParser _parser;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly ITariffRepository _tariffRepo;
    private readonly ILogger<ChargesMessageHandler> _logger;

    public ChargesMessageHandler(
        ICimParser parser,
        IPortfolioRepository portfolioRepo,
        ITariffRepository tariffRepo,
        ILogger<ChargesMessageHandler> logger)
    {
        _parser = parser;
        _portfolioRepo = portfolioRepo;
        _tariffRepo = tariffRepo;
        _logger = logger;
    }

    public QueueName Queue => QueueName.Charges;

    public async Task HandleAsync(DataHubMessage message, CancellationToken ct)
    {
        if (message.MessageType is "RSM-034" or "rsm-034" or "RSM034")
        {
            var tariffData = _parser.ParseRsm034PriceSeries(message.RawPayload);

            await _portfolioRepo.EnsureGridAreaAsync(
                tariffData.GridAreaCode, tariffData.ChargeOwnerId,
                $"Grid {tariffData.GridAreaCode}", CimJsonParser.GridAreaToPriceArea.GetValueOrDefault(tariffData.GridAreaCode, "DK1"), ct);

            await _tariffRepo.SeedGridTariffAsync(
                tariffData.GridAreaCode, tariffData.TariffType, tariffData.ValidFrom, tariffData.Rates, ct);

            await _tariffRepo.SeedSubscriptionAsync(
                tariffData.GridAreaCode, tariffData.SubscriptionType, tariffData.SubscriptionAmountPerMonth,
                tariffData.ValidFrom, ct);

            if (tariffData.ElectricityTaxRate is not null)
            {
                await _tariffRepo.SeedElectricityTaxAsync(
                    tariffData.ElectricityTaxRate.Value, tariffData.ValidFrom, ct);
            }

            _logger.LogInformation(
                "RSM-034: Seeded {RateCount} hourly rates + subscription for grid area {GridArea}",
                tariffData.Rates.Count, tariffData.GridAreaCode);
        }
        else
        {
            _logger.LogInformation("Received Charges message {Type} — not handled yet", message.MessageType);
        }
    }
}
