using DataHub.Settlement.Application.Tariff;

namespace DataHub.Settlement.Application.Parsing;

public record GridTariffResult(
    string GridAreaCode,
    string ChargeOwnerId,
    DateOnly ValidFrom,
    string TariffType,
    IReadOnlyList<TariffRateRow> Rates,
    string SubscriptionType,
    decimal SubscriptionAmountPerMonth);
