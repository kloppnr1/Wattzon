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
/// All portfolio setup (process completion, customer creation, contract, supply period, signup status)
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
    /// Post-transaction: creates aconto invoice and sends RSM-027 customer data update (non-transactional).
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

            // 2. Load signup and create/link customer — all within the same transaction
            var signup = await conn.QuerySingleOrDefaultAsync<FullSignupRow>(
                new CommandDefinition("""
                    SELECT id, signup_number, customer_id, product_id, billing_frequency,
                           customer_name, customer_cpr_cvr, customer_contact_type,
                           billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door,
                           billing_postal_code, billing_city,
                           payer_name, payer_cpr_cvr, payer_contact_type, payer_email, payer_phone,
                           payer_billing_street, payer_billing_house_number, payer_billing_floor,
                           payer_billing_door, payer_billing_postal_code, payer_billing_city
                    FROM portfolio.signup WHERE id = @Id
                    """,
                    new { Id = signupId }, transaction: tx, cancellationToken: ct));

            if (signup is null)
            {
                _logger.LogWarning("RSM-022: Signup {SignupId} not found during activation", signupId);
                await tx.CommitAsync(ct);
                return;
            }

            signupNumber = signup.SignupNumber;
            productId = signup.ProductId;
            customerId = signup.CustomerId;

            // Create/link customer if not yet linked
            if (customerId is null && !string.IsNullOrEmpty(signup.CustomerCprCvr))
            {
                // Check if customer with this CPR/CVR already exists (multi-metering point scenario)
                var existingCustomerId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                    new CommandDefinition(
                        "SELECT id FROM portfolio.customer WHERE cpr_cvr = @CprCvr LIMIT 1",
                        new { CprCvr = signup.CustomerCprCvr }, transaction: tx, cancellationToken: ct));

                var contactType = MapContactType(signup.CustomerContactType ?? "person");

                if (existingCustomerId.HasValue)
                {
                    customerId = existingCustomerId.Value;

                    // Update billing address if provided and customer doesn't have one yet
                    if (signup.BillingStreet is not null || signup.BillingPostalCode is not null || signup.BillingCity is not null)
                    {
                        await conn.ExecuteAsync(
                            new CommandDefinition("""
                                UPDATE portfolio.customer
                                SET billing_dar_id = COALESCE(billing_dar_id, @BillingDarId),
                                    billing_street = COALESCE(billing_street, @BillingStreet),
                                    billing_house_number = COALESCE(billing_house_number, @BillingHouseNumber),
                                    billing_floor = COALESCE(billing_floor, @BillingFloor),
                                    billing_door = COALESCE(billing_door, @BillingDoor),
                                    billing_postal_code = COALESCE(billing_postal_code, @BillingPostalCode),
                                    billing_city = COALESCE(billing_city, @BillingCity),
                                    updated_at = now()
                                WHERE id = @Id AND billing_street IS NULL
                                """,
                                new
                                {
                                    Id = customerId.Value,
                                    signup.BillingDarId, signup.BillingStreet, signup.BillingHouseNumber,
                                    signup.BillingFloor, signup.BillingDoor, signup.BillingPostalCode, signup.BillingCity,
                                },
                                transaction: tx, cancellationToken: ct));
                    }

                    _logger.LogInformation(
                        "RSM-022: Signup {SignupNumber} linked to existing customer {CustomerId} — multi-metering point scenario",
                        signupNumber, customerId);
                }
                else
                {
                    // Create new customer
                    customerId = await conn.QuerySingleAsync<Guid>(
                        new CommandDefinition("""
                            INSERT INTO portfolio.customer (name, cpr_cvr, contact_type,
                                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door,
                                billing_postal_code, billing_city)
                            VALUES (@Name, @CprCvr, @ContactType,
                                @BillingDarId, @BillingStreet, @BillingHouseNumber, @BillingFloor, @BillingDoor,
                                @BillingPostalCode, @BillingCity)
                            RETURNING id
                            """,
                            new
                            {
                                Name = signup.CustomerName, CprCvr = signup.CustomerCprCvr, ContactType = contactType,
                                signup.BillingDarId, signup.BillingStreet, signup.BillingHouseNumber,
                                signup.BillingFloor, signup.BillingDoor, signup.BillingPostalCode, signup.BillingCity,
                            },
                            transaction: tx, cancellationToken: ct));

                    _logger.LogInformation("RSM-022: Customer {CustomerId} created for signup {SignupNumber}",
                        customerId, signupNumber);
                }

                // Link customer to signup
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE portfolio.signup SET customer_id = @CustomerId, updated_at = now() WHERE id = @Id",
                        new { CustomerId = customerId.Value, Id = signupId },
                        transaction: tx, cancellationToken: ct));

                // Create payer if a separate payer was specified at signup
                if (!string.IsNullOrEmpty(signup.PayerName))
                {
                    var payerContactType = MapContactType(signup.PayerContactType ?? "person");
                    await conn.ExecuteAsync(
                        new CommandDefinition("""
                            INSERT INTO portfolio.payer (name, cpr_cvr, contact_type, email, phone,
                                billing_street, billing_house_number, billing_floor, billing_door,
                                billing_postal_code, billing_city)
                            VALUES (@Name, @CprCvr, @ContactType, @Email, @Phone,
                                @BillingStreet, @BillingHouseNumber, @BillingFloor, @BillingDoor,
                                @BillingPostalCode, @BillingCity)
                            """,
                            new
                            {
                                Name = signup.PayerName, CprCvr = signup.PayerCprCvr ?? "",
                                ContactType = payerContactType,
                                Email = signup.PayerEmail, Phone = signup.PayerPhone,
                                BillingStreet = signup.PayerBillingStreet,
                                BillingHouseNumber = signup.PayerBillingHouseNumber,
                                BillingFloor = signup.PayerBillingFloor,
                                BillingDoor = signup.PayerBillingDoor,
                                BillingPostalCode = signup.PayerBillingPostalCode,
                                BillingCity = signup.PayerBillingCity,
                            },
                            transaction: tx, cancellationToken: ct));

                    _logger.LogInformation("RSM-022: Payer created for signup {SignupNumber}", signupNumber);
                }
            }

            if (customerId is null)
            {
                _logger.LogWarning(
                    "RSM-022: Signup {SignupId} has no customer after activation — portfolio not created", signupId);
                await tx.CommitAsync(ct);
                return;
            }

            // 3. Update signup status to 'active'
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE portfolio.signup SET status = 'active', updated_at = now() WHERE id = @Id",
                    new { Id = signupId }, transaction: tx, cancellationToken: ct));

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
                        BillingFrequency = signup.BillingFrequency,
                        PaymentModel = "aconto",
                        StartDate = effectiveDate,
                    },
                    transaction: tx, cancellationToken: ct));

            // 5. Create SupplyPeriod
            await conn.ExecuteAsync(
                new CommandDefinition("""
                    INSERT INTO portfolio.supply_period (gsrn, start_date)
                    VALUES (@Gsrn, @StartDate)
                    ON CONFLICT DO NOTHING
                    """,
                    new { Gsrn = meteringPointId, StartDate = effectiveDate },
                    transaction: tx, cancellationToken: ct));

            // 6. Check if we need to send RSM-027
            if (processType is "supplier_switch" or "move_in" && datahubCorrelationId is not null)
            {
                cprCvr = signup.CustomerCprCvr;
                if (cprCvr is not null)
                {
                    var custName = await conn.QuerySingleOrDefaultAsync<string?>(
                        new CommandDefinition(
                            "SELECT name FROM portfolio.customer WHERE id = @Id",
                            new { Id = customerId.Value }, transaction: tx, cancellationToken: ct));

                    shouldSendRsm027 = true;
                    customerName = custName ?? signupNumber;
                }
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Post-commit: idempotent sync via OnboardingService (safety net — data already committed)
        try
        {
            await _onboardingService.SyncFromProcessAsync(processId, "completed", null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RSM-022: Post-commit signup sync failed for process {ProcessId} — portfolio data already committed",
                processId);
        }

        // 7. Create aconto invoice (uses its own transaction internally via InvoiceRepository)
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

        // 8. Send RSM-027 outside transaction (HTTP call)
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

    private static string MapContactType(string signupContactType)
    {
        return signupContactType switch
        {
            "person" => "private",
            "company" => "business",
            _ => signupContactType,
        };
    }

    private record ProcessRow(Guid Id, string Status);
    private record ContractRow(Guid Id, Guid? PayerId);

    // Extended signup row with all fields needed for customer creation within the transaction
    private class FullSignupRow
    {
        public Guid Id { get; set; }
        public string? SignupNumber { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? ProductId { get; set; }
        public string BillingFrequency { get; set; } = null!;
        public string? CustomerName { get; set; }
        public string? CustomerCprCvr { get; set; }
        public string? CustomerContactType { get; set; }
        public string? BillingDarId { get; set; }
        public string? BillingStreet { get; set; }
        public string? BillingHouseNumber { get; set; }
        public string? BillingFloor { get; set; }
        public string? BillingDoor { get; set; }
        public string? BillingPostalCode { get; set; }
        public string? BillingCity { get; set; }
        public string? PayerName { get; set; }
        public string? PayerCprCvr { get; set; }
        public string? PayerContactType { get; set; }
        public string? PayerEmail { get; set; }
        public string? PayerPhone { get; set; }
        public string? PayerBillingStreet { get; set; }
        public string? PayerBillingHouseNumber { get; set; }
        public string? PayerBillingFloor { get; set; }
        public string? PayerBillingDoor { get; set; }
        public string? PayerBillingPostalCode { get; set; }
        public string? PayerBillingCity { get; set; }
    }
}
