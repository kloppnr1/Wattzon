using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class SettlementOrchestrationService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly IProcessRepository _processRepo;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IMeteringCompletenessChecker _completenessChecker;
    private readonly ISettlementDataLoader _dataLoader;
    private readonly ISettlementEngine _engine;
    private readonly ISettlementResultStore _resultStore;
    private readonly IClock _clock;
    private readonly ILogger<SettlementOrchestrationService> _logger;

    public SettlementOrchestrationService(
        IProcessRepository processRepo,
        IPortfolioRepository portfolioRepo,
        IMeteringCompletenessChecker completenessChecker,
        ISettlementDataLoader dataLoader,
        ISettlementEngine engine,
        ISettlementResultStore resultStore,
        IClock clock,
        ILogger<SettlementOrchestrationService> logger)
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Settlement orchestration tick failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        var completedProcesses = await _processRepo.GetByStatusAsync("completed", ct);

        foreach (var process in completedProcesses)
        {
            try
            {
                await TrySettleAsync(process, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to settle process {ProcessId} for GSRN {Gsrn}", process.Id, process.Gsrn);
            }
        }
    }

    private async Task TrySettleAsync(ProcessRequest process, CancellationToken ct)
    {
        if (!process.EffectiveDate.HasValue) return;

        // Get contract first — we need billing_frequency to calculate billing periods
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

        // Iterate over all billing periods from the effective date forward.
        // Settle the first unsettled period that has complete metering data.
        while (true)
        {
            var periodEnd = BillingPeriodCalculator.GetFirstPeriodEnd(periodStart, contract.BillingFrequency);

            // Don't settle periods that haven't closed yet
            if (periodEnd >= today)
                break;

            // Skip periods that already have a settlement run
            if (await _resultStore.HasSettlementRunAsync(process.Gsrn, periodStart, periodEnd, ct))
            {
                periodStart = periodEnd.AddDays(1);
                continue;
            }

            var startDt = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endDt = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            // Check metering completeness
            var completeness = await _completenessChecker.CheckAsync(process.Gsrn, startDt, endDt, ct);
            if (!completeness.IsComplete)
            {
                _logger.LogDebug("GSRN {Gsrn}: metering incomplete for {PeriodStart}–{PeriodEnd} ({Received}/{Expected})",
                    process.Gsrn, periodStart, periodEnd, completeness.ReceivedHours, completeness.ExpectedHours);
                break; // Can't settle future periods if the current one is incomplete
            }

            // Load data and calculate
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

            periodStart = periodEnd.AddDays(1);
        }
    }
}
