namespace DataHub.Settlement.Application.Tariff;

public interface ITariffRepository
{
    Task<IReadOnlyList<TariffRateRow>> GetRatesAsync(
        string gridAreaCode, string tariffType, DateOnly date, CancellationToken ct);

    Task<decimal> GetSubscriptionAsync(
        string gridAreaCode, string subscriptionType, DateOnly date, CancellationToken ct);

    Task<decimal> GetElectricityTaxAsync(DateOnly date, CancellationToken ct);

    Task SeedGridTariffAsync(
        string gridAreaCode, string tariffType, DateOnly validFrom,
        IReadOnlyList<TariffRateRow> rates, CancellationToken ct);

    Task SeedSubscriptionAsync(
        string gridAreaCode, string subscriptionType, decimal amountPerMonth,
        DateOnly validFrom, CancellationToken ct);

    Task SeedElectricityTaxAsync(decimal ratePerKwh, DateOnly validFrom, CancellationToken ct);
}
