using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class CorrectionService : ICorrectionService
{
    private readonly CorrectionEngine _engine;
    private readonly ICorrectionRepository _correctionRepo;
    private readonly IMeteringDataRepository _meteringRepo;
    private readonly ISpotPriceRepository _spotPriceRepo;
    private readonly ITariffRepository _tariffRepo;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IBillingRepository _billingRepo;

    public CorrectionService(
        CorrectionEngine engine,
        ICorrectionRepository correctionRepo,
        IMeteringDataRepository meteringRepo,
        ISpotPriceRepository spotPriceRepo,
        ITariffRepository tariffRepo,
        IPortfolioRepository portfolioRepo,
        IBillingRepository billingRepo)
    {
        _engine = engine;
        _correctionRepo = correctionRepo;
        _meteringRepo = meteringRepo;
        _spotPriceRepo = spotPriceRepo;
        _tariffRepo = tariffRepo;
        _portfolioRepo = portfolioRepo;
        _billingRepo = billingRepo;
    }

    public async Task<CorrectionBatchDetail> TriggerCorrectionAsync(TriggerCorrectionRequest request, CancellationToken ct)
    {
        var from = request.PeriodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = request.PeriodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // 1. Get metering data changes
        var changes = await _meteringRepo.GetChangesAsync(request.MeteringPointId, from, to, ct);
        if (changes.Count == 0)
            throw new InvalidOperationException("No metering data changes found for the specified metering point and period.");

        // 2. Convert to ConsumptionDelta list
        var deltas = changes.Select(c => new ConsumptionDelta(c.Timestamp, c.PreviousKwh, c.NewKwh)).ToList();

        // 3. Look up active contract to get product (margin, supplement)
        var contract = await _portfolioRepo.GetActiveContractAsync(request.MeteringPointId, ct)
            ?? throw new InvalidOperationException($"No active contract found for metering point {request.MeteringPointId}.");

        var product = await _portfolioRepo.GetProductAsync(contract.ProductId, ct)
            ?? throw new InvalidOperationException($"Product {contract.ProductId} not found.");

        // 4. Look up metering point for grid area and price area
        var meteringPoint = (await _portfolioRepo.GetMeteringPointsForCustomerAsync(contract.CustomerId, ct))
            .FirstOrDefault(mp => mp.Gsrn == request.MeteringPointId)
            ?? throw new InvalidOperationException($"Metering point {request.MeteringPointId} not found for customer {contract.CustomerId}.");
        var gridAreaCode = meteringPoint.GridAreaCode;
        var priceArea = meteringPoint.PriceArea;

        // 5. Load spot prices
        var spotPrices = await _spotPriceRepo.GetPricesAsync(priceArea, from, to, ct);

        var gridTariffRates = await _tariffRepo.GetRatesAsync(gridAreaCode, "grid", request.PeriodStart, ct);
        var systemRates = await _tariffRepo.GetRatesAsync(gridAreaCode, "system", request.PeriodStart, ct);
        var transmissionRates = await _tariffRepo.GetRatesAsync(gridAreaCode, "transmission", request.PeriodStart, ct);

        if (systemRates.Count == 0)
            throw new InvalidOperationException($"No system tariff rates found for grid area {gridAreaCode} on {request.PeriodStart}.");
        if (transmissionRates.Count == 0)
            throw new InvalidOperationException($"No transmission tariff rates found for grid area {gridAreaCode} on {request.PeriodStart}.");

        var electricityTaxRate = await _tariffRepo.GetElectricityTaxAsync(request.PeriodStart, ct)
            ?? throw new InvalidOperationException($"No electricity tax rate found for {request.PeriodStart}.");

        var systemTariffRate = systemRates[0].PricePerKwh;
        var transmissionTariffRate = transmissionRates[0].PricePerKwh;

        // 6. Build CorrectionRequest and calculate
        var correctionRequest = new CorrectionRequest(
            request.MeteringPointId,
            request.PeriodStart,
            request.PeriodEnd,
            deltas,
            spotPrices,
            gridTariffRates,
            systemTariffRate,
            transmissionTariffRate,
            electricityTaxRate,
            product.MarginOrePerKwh / 100m,
            (product.SupplementOrePerKwh ?? 0m) / 100m);

        var result = _engine.Calculate(correctionRequest);

        // 7. Find original_run_id by querying settlement runs for this GSRN covering the period
        Guid? originalRunId = null;
        var runs = await _billingRepo.GetSettlementRunsAsync(
            billingPeriodId: null, status: null, meteringPointId: request.MeteringPointId,
            gridAreaCode: null, fromDate: request.PeriodStart, toDate: request.PeriodEnd,
            page: 1, pageSize: 1, ct);
        if (runs.Items.Count > 0)
            originalRunId = runs.Items[0].Id;

        // 8. Store correction
        var batchId = Guid.NewGuid();
        await _correctionRepo.StoreCorrectionAsync(batchId, result, originalRunId, "manual", request.Note, ct);

        // 9. Return detail
        var detail = await _correctionRepo.GetCorrectionAsync(batchId, ct);
        return detail!;
    }
}
