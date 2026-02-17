using System;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Domain;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class SettlementTriggerService
{
    private readonly IProcessRepository _processRepo;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IMeteringCompletenessChecker _completenessChecker;
    private readonly ISettlementDataLoader _dataLoader;
    private readonly ISettlementEngine _engine;
    private readonly ISettlementResultStore _resultStore;
    private readonly IClock _clock;
    private readonly ILogger<SettlementTriggerService> _logger;

    public SettlementTriggerService(
        IProcessRepository processRepo,
        IPortfolioRepository portfolioRepo,
        IMeteringCompletenessChecker completenessChecker,
        ISettlementDataLoader dataLoader,
        ISettlementEngine engine,
        ISettlementResultStore resultStore,
        IClock clock,
        ILogger<SettlementTriggerService> logger)
    {
        _processRepo = processRepo;
        _portfolioRepo = portfolioRepo;
        _completenessChecker = completenessChecker;
        _dataLoader = dataLoader;
        _engine = engine;
        _resultStore = resultStore;
        _clock = clock;
        _logger = logger;
    }

    public async Task TrySettleAsync(string gsrn, CancellationToken ct)
    {
        var process = await _processRepo.GetCompletedByGsrnAsync(gsrn, ct);
        if (process is null) return;

        await TrySettleProcessAsync(process, ct);
    }

    internal async Task TrySettleProcessAsync(ProcessRequest process, CancellationToken ct)
    {
        if (!process.EffectiveDate.HasValue) return;

        var contract = await _portfolioRepo.GetActiveContractAsync(process.Gsrn, ct);
        if (contract is null)
        {
            _logger.LogWarning("GSRN {Gsrn}: no active contract found, skipping settlement", process.Gsrn);
            return;
        }

        var mp = await _portfolioRepo.GetMeteringPointByGsrnAsync(process.Gsrn, ct);
        if (mp is null)
        {
            _logger.LogWarning("GSRN {Gsrn}: metering point not found, skipping settlement", process.Gsrn);
            return;
        }

        var product = await _portfolioRepo.GetProductAsync(contract.ProductId, ct);
        if (product is null)
        {
            _logger.LogWarning("GSRN {Gsrn}: product {ProductId} not found, skipping settlement", process.Gsrn, contract.ProductId);
            return;
        }

        var today = _clock.Today;
        var periodStart = process.EffectiveDate.Value;

        while (true)
        {
            var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(periodStart, contract.BillingFrequency);

            if (periodEnd > today)
                break;

            if (await _resultStore.HasSettlementRunAsync(process.Gsrn, periodStart, periodEnd, ct))
            {
                periodStart = periodEnd;
                continue;
            }

            var startDt = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endDt = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            var completeness = await _completenessChecker.CheckAsync(process.Gsrn, startDt, endDt, ct);
            if (!completeness.IsComplete)
            {
                _logger.LogDebug("GSRN {Gsrn}: metering incomplete for {PeriodStart}-{PeriodEnd} ({Received}/{Expected})",
                    process.Gsrn, periodStart, periodEnd, completeness.ReceivedHours, completeness.ExpectedHours);
                break;
            }

            try
            {
                var input = await _dataLoader.LoadAsync(
                    process.Gsrn, mp.GridAreaCode, mp.PriceArea,
                    periodStart, periodEnd,
                    product.MarginOrePerKwh / 100m,
                    (product.SupplementOrePerKwh ?? 0m) / 100m,
                    product.SubscriptionKrPerMonth,
                    ct);

                var request = new SettlementRequest(
                    input.MeteringPointId, input.PeriodStart, input.PeriodEnd,
                    input.Consumption, input.SpotPrices, input.GridTariffRates,
                    input.SystemTariffRate, input.TransmissionTariffRate, input.ElectricityTaxRate,
                    input.GridSubscriptionPerMonth, input.MarginPerKwh, input.SupplementPerKwh,
                    input.SupplierSubscriptionPerMonth, input.Elvarme);

                var result = _engine.Calculate(request);
                await _resultStore.StoreAsync(process.Gsrn, mp.GridAreaCode, result, contract.BillingFrequency, ct);

                _logger.LogInformation("Settlement completed for GSRN {Gsrn}: {PeriodStart} to {PeriodEnd}, total {Total} DKK",
                    process.Gsrn, periodStart, periodEnd, result.Total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Settlement failed for GSRN {Gsrn}: {PeriodStart} to {PeriodEnd}",
                    process.Gsrn, periodStart, periodEnd);

                await _resultStore.StoreFailedRunAsync(process.Gsrn, mp.GridAreaCode, periodStart, periodEnd, ex.Message, ct);
                
                // Don't rethrow - we've persisted the failure, so we can continue with the next period
                break;
            }

            periodStart = periodEnd;
        }
    }
}
