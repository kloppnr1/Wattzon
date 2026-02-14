using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
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
    private readonly ILogger<SettlementOrchestrationService> _logger;

    public SettlementOrchestrationService(
        IProcessRepository processRepo,
        IPortfolioRepository portfolioRepo,
        IMeteringCompletenessChecker completenessChecker,
        ISettlementDataLoader dataLoader,
        ISettlementEngine engine,
        ISettlementResultStore resultStore,
        ILogger<SettlementOrchestrationService> logger)
    {
        _processRepo = processRepo;
        _portfolioRepo = portfolioRepo;
        _completenessChecker = completenessChecker;
        _dataLoader = dataLoader;
        _engine = engine;
        _resultStore = resultStore;
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

        var periodStart = process.EffectiveDate.Value;
        var periodEnd = periodStart.AddMonths(1);
        var startDt = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endDt = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Check metering completeness
        var completeness = await _completenessChecker.CheckAsync(process.Gsrn, startDt, endDt, ct);
        if (!completeness.IsComplete)
        {
            _logger.LogDebug("GSRN {Gsrn}: metering incomplete ({Received}/{Expected})",
                process.Gsrn, completeness.ReceivedHours, completeness.ExpectedHours);
            return;
        }

        // Get contract to determine product pricing
        var contract = await _portfolioRepo.GetActiveContractAsync(process.Gsrn, ct);
        if (contract is null)
        {
            _logger.LogWarning("GSRN {Gsrn}: no active contract found, skipping settlement", process.Gsrn);
            return;
        }

        var product = await _portfolioRepo.GetProductAsync(contract.ProductId, ct);
        if (product is null)
        {
            _logger.LogWarning("GSRN {Gsrn}: product {ProductId} not found, skipping settlement", process.Gsrn, contract.ProductId);
            return;
        }

        // Load data and calculate
        var input = await _dataLoader.LoadAsync(
            process.Gsrn, "344", "DK1",
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
        await _resultStore.StoreAsync(process.Gsrn, "344", result, contract.BillingFrequency, ct);

        _logger.LogInformation("Settlement completed for GSRN {Gsrn}: {PeriodStart} to {PeriodEnd}, total {Total} DKK",
            process.Gsrn, periodStart, periodEnd, result.Total);
    }
}
