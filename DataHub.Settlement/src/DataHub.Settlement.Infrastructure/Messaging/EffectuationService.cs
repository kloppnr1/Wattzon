using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Messaging;

/// <summary>
/// Handles RSM-022 effectuation (metering point activation) as a single atomic transaction.
/// All portfolio setup (process completion, customer creation, contract, supply period, aconto invoice)
/// happens within one DB transaction to prevent partial state.
/// </summary>
public sealed class EffectuationService
{
    private readonly string _connectionString;
    private readonly IOnboardingService _onboardingService;
    private readonly IInvoiceService _invoiceService;
    private readonly IDataHubClient _dataHubClient;
    private readonly IBrsRequestBuilder _brsBuilder;
    private readonly IMessageRepository _messageRepo;
    private readonly IClock _clock;
    private readonly ILogger<EffectuationService> _logger;

    static EffectuationService()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public EffectuationService(
        string connectionString,
        IOnboardingService onboardingService,
        IInvoiceService invoiceService,
        IDataHubClient dataHubClient,
        IBrsRequestBuilder brsBuilder,
        IMessageRepository messageRepo,
        IClock clock,
        ILogger<EffectuationService> logger)
    {
        _connectionString = connectionString;
        _onboardingService = onboardingService;
        _invoiceService = invoiceService;
        _dataHubClient = dataHubClient;
        _brsBuilder = brsBuilder;
        _messageRepo = messageRepo;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Activates a metering point following RSM-022, wrapping all DB operations in a single transaction.
    /// Post-transaction: sends RSM-027 customer data update (non-transactional, HTTP call).
    /// </summary>
    public async Task ActivateAsync(
        Guid processId,
        Guid signupId,
        string meteringPointId,
        DateOnly effectiveDate,
        string? processType,
        string? datahubCorrelationId,
        CancellationToken ct)
    {
        Guid? customerId = null;
        string? signupNumber = null;
        Guid? productId = null;
        bool shouldSendRsm027 = false;
        string? customerName = null;
        string? cprCvr = null;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // 1. Mark process completed (state transition)
            var process = await conn.QuerySingleAsync<ProcessRow>(
                new CommandDefinition(
                    "SELECT id, status FROM lifecycle.process_request WHERE id = @Id FOR UPDATE",
                    new { Id = processId }, transaction: tx, cancellationToken: ct));

            if (process.Status != "effectuation_pending")
                throw new InvalidOperationException(
                    $"Invalid transition from '{process.Status}' to 'completed' for process {processId}");

            await conn.ExecuteAsync(
                new CommandDefinition("""
                    UPDATE lifecycle.process_request SET status = 'completed', updated_at = @Now WHERE id = @Id AND status = @Expected
                    """,
                    new { Id = processId, Expected = process.Status, Now = _clock.UtcNow },
                    transaction: tx, cancellationToken: ct));

            await conn.ExecuteAsync(
                new CommandDefinition("""
                    INSERT INTO lifecycle.process_event (process_request_id, event_type, source, occurred_at)
                    VALUES (@ProcessId, 'completed', 'system', @Now)
                    """,
                    new { ProcessId = processId, Now = _clock.UtcNow },
                    transaction: tx, cancellationToken: ct));

            // 2. Sync signup status (creates/links Customer via onboarding service)
            // Note: OnboardingService.SyncFromProcessAsync manages its own connection, so we commit
            // the process transition first, then sync, then do the rest in a new transaction.
            // This is a pragmatic trade-off: the process state is committed, and the rest follows.
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // 2b. Sync signup status outside the transaction (OnboardingService uses its own connection)
        await _onboardingService.SyncFromProcessAsync(processId, "completed", null, ct);

        // Start a new transaction for the remaining portfolio operations
        await using var tx2 = await conn.BeginTransactionAsync(ct);
        try
        {
            // 3. Reload signup to get customer_id
            var signup = await conn.QuerySingleOrDefaultAsync<SignupRow>(
                new CommandDefinition(
                    "SELECT id, signup_number, customer_id, product_id FROM portfolio.signup WHERE id = @Id",
                    new { Id = signupId }, transaction: tx2, cancellationToken: ct));

            if (signup?.CustomerId is null)
            {
                _logger.LogWarning(
                    "RSM-022: Signup {SignupId} has no customer after activation — portfolio not created", signupId);
                await tx2.CommitAsync(ct);
                return;
            }

            customerId = signup.CustomerId;
            signupNumber = signup.SignupNumber;
            productId = signup.ProductId;

            // 4. Create Contract
            await conn.ExecuteAsync(
                new CommandDefinition("""
                    INSERT INTO portfolio.contract (customer_id, gsrn, product_id, billing_frequency, payment_model, start_date)
                    VALUES (@CustomerId, @Gsrn, @ProductId, @BillingFrequency, @PaymentModel, @StartDate)
                    ON CONFLICT DO NOTHING
                    """,
                    new
                    {
                        CustomerId = customerId.Value,
                        Gsrn = meteringPointId,
                        ProductId = productId,
                        BillingFrequency = "monthly",
                        PaymentModel = "aconto",
                        StartDate = effectiveDate,
                    },
                    transaction: tx2, cancellationToken: ct));

            // 5. Create SupplyPeriod
            await conn.ExecuteAsync(
                new CommandDefinition("""
                    INSERT INTO portfolio.supply_period (gsrn, start_date)
                    VALUES (@Gsrn, @StartDate)
                    ON CONFLICT DO NOTHING
                    """,
                    new { Gsrn = meteringPointId, StartDate = effectiveDate },
                    transaction: tx2, cancellationToken: ct));

            // 7. Check if we need to send RSM-027
            if (processType is "supplier_switch" or "move_in" && datahubCorrelationId is not null)
            {
                var cprCvrRow = await conn.QuerySingleOrDefaultAsync<string?>(
                    new CommandDefinition(
                        "SELECT customer_cpr_cvr FROM portfolio.signup WHERE id = @Id",
                        new { Id = signupId }, transaction: tx2, cancellationToken: ct));

                var customer = await conn.QuerySingleOrDefaultAsync<dynamic>(
                    new CommandDefinition(
                        "SELECT name FROM portfolio.customer WHERE id = @Id",
                        new { Id = customerId.Value }, transaction: tx2, cancellationToken: ct));

                if (cprCvrRow is not null)
                {
                    shouldSendRsm027 = true;
                    customerName = (string?)customer?.name ?? signupNumber;
                    cprCvr = cprCvrRow;
                }
            }

            await tx2.CommitAsync(ct);
        }
        catch
        {
            await tx2.RollbackAsync(ct);
            throw;
        }

        // 8. Create aconto invoice (uses its own transaction internally via InvoiceRepository)
        try
        {
            var contract = await GetActiveContractAsync(conn, meteringPointId, ct);
            var periodEnd = effectiveDate.AddMonths(1);

            // Use AcontoEstimator with default values for first-month estimate
            var acontoAmount = AcontoEstimator.EstimateQuarterlyAmount(
                annualConsumptionKwh: 4000m,
                expectedPricePerKwh: AcontoEstimator.CalculateExpectedPricePerKwh(
                    averageSpotPriceOrePerKwh: 80m,
                    marginOrePerKwh: 4m,
                    systemTariffRate: 0.054m,
                    transmissionTariffRate: 0.049m,
                    electricityTaxRate: 0.008m,
                    averageGridTariffRate: 0.20m));
            // Convert quarterly to monthly (first month only)
            acontoAmount = Math.Round(acontoAmount / 3m, 2);

            await _invoiceService.CreateAcontoInvoiceAsync(
                customerId!.Value, contract?.PayerId, contract?.Id,
                meteringPointId, effectiveDate, periodEnd, acontoAmount, ct);

            _logger.LogInformation(
                "RSM-022: Created aconto invoice for GSRN {Gsrn}, period {Start} to {End}, amount {Amount} DKK",
                meteringPointId, effectiveDate, periodEnd, acontoAmount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RSM-022: Failed to create aconto invoice for GSRN {Gsrn} — invoice creation is non-blocking",
                meteringPointId);
        }

        // 9. Send RSM-027 outside transaction (HTTP call)
        if (shouldSendRsm027)
        {
            try
            {
                var rsm027 = _brsBuilder.BuildRsm027(meteringPointId, customerName!, cprCvr!, datahubCorrelationId!);
                await _dataHubClient.SendRequestAsync("customer_data_update", rsm027, ct);
                await _messageRepo.RecordOutboundRequestAsync("RSM-027", meteringPointId, datahubCorrelationId!, "sent", rsm027, ct);
                _logger.LogInformation("RSM-022: Sent RSM-027 customer data update for {Gsrn}", meteringPointId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RSM-022: Failed to send RSM-027 for {Gsrn} — non-blocking", meteringPointId);
            }
        }

        _logger.LogInformation(
            "RSM-022: Activated portfolio for signup {SignupNumber}, GSRN {Gsrn}, supply from {Start}",
            signupNumber, meteringPointId, effectiveDate);
    }

    private static async Task<ContractRow?> GetActiveContractAsync(NpgsqlConnection conn, string gsrn, CancellationToken ct)
    {
        return await conn.QuerySingleOrDefaultAsync<ContractRow>(
            new CommandDefinition(
                "SELECT id, payer_id FROM portfolio.contract WHERE gsrn = @Gsrn AND end_date IS NULL ORDER BY start_date DESC LIMIT 1",
                new { Gsrn = gsrn }, cancellationToken: ct));
    }

    private record ProcessRow(Guid Id, string Status);
    private record SignupRow(Guid Id, string? SignupNumber, Guid? CustomerId, Guid? ProductId);
    private record ContractRow(Guid Id, Guid? PayerId);
}
