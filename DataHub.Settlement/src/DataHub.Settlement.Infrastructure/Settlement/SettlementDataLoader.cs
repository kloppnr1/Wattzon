using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class SettlementDataLoader : ISettlementDataLoader
{
    private readonly IMeteringDataRepository _meteringRepo;
    private readonly ISpotPriceRepository _spotPriceRepo;
    private readonly ITariffRepository _tariffRepo;

    public SettlementDataLoader(
        IMeteringDataRepository meteringRepo,
        ISpotPriceRepository spotPriceRepo,
        ITariffRepository tariffRepo)
    {
        _meteringRepo = meteringRepo;
        _spotPriceRepo = spotPriceRepo;
        _tariffRepo = tariffRepo;
    }

    public async Task<SettlementInput> LoadAsync(
        string gsrn, string gridAreaCode, string priceArea,
        DateOnly periodStart, DateOnly periodEnd,
        decimal marginPerKwh, decimal supplementPerKwh, decimal supplierSubscriptionPerMonth,
        CancellationToken ct)
    {
        var start = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var midDate = periodStart.AddDays((periodEnd.DayNumber - periodStart.DayNumber) / 2);

        var consumptionTask = _meteringRepo.GetConsumptionAsync(gsrn, start, end, ct);
        var spotPricesTask = _spotPriceRepo.GetPricesAsync(priceArea, start, end, ct);
        var ratesTask = _tariffRepo.GetRatesAsync(gridAreaCode, "grid", midDate, ct);
        var elTaxTask = _tariffRepo.GetElectricityTaxAsync(midDate, ct);
        var gridSubTask = _tariffRepo.GetSubscriptionAsync(gridAreaCode, "grid", midDate, ct);

        await Task.WhenAll(consumptionTask, spotPricesTask, ratesTask, elTaxTask, gridSubTask);

        return new SettlementInput(
            gsrn, periodStart, periodEnd,
            await consumptionTask,
            await spotPricesTask,
            await ratesTask,
            SystemTariffRate: 0.054m,
            TransmissionTariffRate: 0.049m,
            ElectricityTaxRate: await elTaxTask,
            GridSubscriptionPerMonth: await gridSubTask,
            MarginPerKwh: marginPerKwh,
            SupplementPerKwh: supplementPerKwh,
            SupplierSubscriptionPerMonth: supplierSubscriptionPerMonth);
    }
}
