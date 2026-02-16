using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Domain;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Onboarding;

public sealed class OnboardingService : IOnboardingService
{
    private readonly ISignupRepository _signupRepo;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IProcessRepository _processRepo;
    private readonly IAddressLookupClient _addressLookup;
    private readonly IDataHubClient _dataHubClient;
    private readonly IBrsRequestBuilder _brsBuilder;
    private readonly IMessageRepository _messageRepo;
    private readonly IClock _clock;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        ISignupRepository signupRepo,
        IPortfolioRepository portfolioRepo,
        IProcessRepository processRepo,
        IAddressLookupClient addressLookup,
        IDataHubClient dataHubClient,
        IBrsRequestBuilder brsBuilder,
        IMessageRepository messageRepo,
        IClock clock,
        ILogger<OnboardingService> logger)
    {
        _signupRepo = signupRepo;
        _portfolioRepo = portfolioRepo;
        _processRepo = processRepo;
        _addressLookup = addressLookup;
        _dataHubClient = dataHubClient;
        _brsBuilder = brsBuilder;
        _messageRepo = messageRepo;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AddressLookupResponse> LookupAddressAsync(string darId, CancellationToken ct)
    {
        var result = await _addressLookup.LookupByDarIdAsync(darId, ct);

        var meteringPoints = new List<MeteringPointResponse>();
        foreach (var mp in result.MeteringPoints)
        {
            var hasActive = await _processRepo.HasActiveByGsrnAsync(mp.Gsrn, ct);
            meteringPoints.Add(new MeteringPointResponse(mp.Gsrn, mp.Type, mp.GridAreaCode, hasActive));
        }

        return new AddressLookupResponse(meteringPoints);
    }

    public async Task<AddressLookupResponse> ValidateGsrnAsync(string gsrn, CancellationToken ct)
    {
        if (!GsrnValidator.IsValid(gsrn))
            throw new ValidationException($"Invalid GSRN format: {gsrn}");

        var hasActive = await _processRepo.HasActiveByGsrnAsync(gsrn, ct);
        var mp = new MeteringPointResponse(gsrn, "E17", "\u2014", hasActive);
        return new AddressLookupResponse([mp]);
    }

    public async Task<SignupResponse> CreateSignupAsync(SignupRequest request, CancellationToken ct)
    {
        // 0. If this is a correction, validate the original signup exists and is rejected
        if (request.CorrectedFromId.HasValue)
        {
            var original = await _signupRepo.GetByIdAsync(request.CorrectedFromId.Value, ct)
                ?? throw new ValidationException($"Original signup {request.CorrectedFromId} not found.");

            if (original.Status != "rejected")
                throw new ValidationException($"Can only correct a rejected signup. Signup {original.SignupNumber} is '{original.Status}'.");
        }

        // 1. Resolve GSRN — either directly or via DAR ID
        string gsrn;
        if (string.IsNullOrEmpty(request.DarId))
        {
            // GSRN-direct mode — no address lookup
            gsrn = request.Gsrn
                ?? throw new ValidationException("Either DarId or Gsrn must be provided.");
        }
        else
        {
            // Existing DAR-based flow
            var lookupResult = await _addressLookup.LookupByDarIdAsync(request.DarId, ct);

            if (lookupResult.MeteringPoints.Count == 0)
                throw new ValidationException("No metering point found for the given address.");

            if (!string.IsNullOrEmpty(request.Gsrn))
            {
                // Frontend selected a specific GSRN — validate it belongs to this address
                var match = lookupResult.MeteringPoints.FirstOrDefault(mp => mp.Gsrn == request.Gsrn);
                gsrn = match?.Gsrn
                    ?? throw new ValidationException($"GSRN {request.Gsrn} not found at address {request.DarId}.");
            }
            else if (lookupResult.MeteringPoints.Count == 1)
            {
                gsrn = lookupResult.MeteringPoints[0].Gsrn;
            }
            else
            {
                throw new ValidationException(
                    $"Multiple metering points found at this address ({lookupResult.MeteringPoints.Count}). Please select one.");
            }
        }

        // 2. Validate GSRN format
        if (!GsrnValidator.IsValid(gsrn))
            throw new ValidationException($"Invalid GSRN format: {gsrn}");

        // 3. Validate customer name is not empty (BRS-009 D03)
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new ValidationException("Customer name is required.");

        // 3a. Validate mobile phone is provided
        if (string.IsNullOrWhiteSpace(request.Mobile))
            throw new ValidationException("Mobile phone number is required.");

        // 3b. Validate CPR/CVR format based on contact type
        var dbContactTypeForValidation = MapContactTypeToDb(request.ContactType);
        if (dbContactTypeForValidation == "person")
        {
            if (string.IsNullOrEmpty(request.CprCvr) || request.CprCvr.Length != 10 || !request.CprCvr.All(char.IsDigit))
                throw new ValidationException("CPR must be exactly 10 digits.");
        }
        else if (dbContactTypeForValidation == "company")
        {
            if (string.IsNullOrEmpty(request.CprCvr) || request.CprCvr.Length != 8 || !request.CprCvr.All(char.IsDigit))
                throw new ValidationException("CVR must be exactly 8 digits.");
        }

        // 4. Check metering point exists and is eligible (BRS-001 E10/D16, BRS-009 E10/D16)
        var meteringPoint = await _portfolioRepo.GetMeteringPointByGsrnAsync(gsrn, ct);
        if (meteringPoint is not null && meteringPoint.ConnectionStatus == "closed_down")
            throw new ValidationException($"Metering point {gsrn} is closed down and not eligible for {request.Type}.");

        // 5. Check no active signup for this GSRN
        var existing = await _signupRepo.GetActiveByGsrnAsync(gsrn, ct);
        if (existing is not null)
            throw new ValidationException($"An active signup already exists for GSRN {gsrn} ({existing.SignupNumber}).");

        // 6. Validate product
        var product = await _portfolioRepo.GetProductAsync(request.ProductId, ct)
            ?? throw new ValidationException($"Product {request.ProductId} not found.");

        // 7. Validate type
        if (request.Type is not ("switch" or "move_in"))
            throw new ValidationException($"Invalid type '{request.Type}'. Must be 'switch' or 'move_in'.");

        // 8. Validate effective date per BRS-001/BRS-009 timing rules
        ValidateEffectiveDate(request.Type, request.EffectiveDate);

        // 8b. Validate billing frequency
        var billingFrequency = request.BillingFrequency ?? "monthly";
        if (billingFrequency is not ("daily" or "weekly" or "monthly" or "quarterly"))
            throw new ValidationException($"Invalid billing frequency '{billingFrequency}'. Must be 'daily', 'weekly', 'monthly', or 'quarterly'.");

        // 8c. Validate payment model
        var paymentModel = request.PaymentModel ?? "post_payment";
        if (paymentModel is not ("aconto" or "post_payment"))
            throw new ValidationException($"Invalid payment model '{paymentModel}'. Must be 'aconto' or 'post_payment'.");

        // 9. Map type to process type
        var processType = request.Type == "switch" ? "supplier_switch" : "move_in";

        // 10. Create process request
        var stateMachine = new ProcessStateMachine(_processRepo, _clock);
        var process = await stateMachine.CreateRequestAsync(gsrn, processType, request.EffectiveDate, ct);

        // 11. Create signup (with customer info + address/payer, but customer not created yet)
        var dbContactType = MapContactTypeToDb(request.ContactType);
        var addressInfo = new SignupAddressInfo(
            request.BillingDarId,
            request.BillingStreet, request.BillingHouseNumber, request.BillingFloor,
            request.BillingDoor, request.BillingPostalCode, request.BillingCity,
            request.PayerName, request.PayerCprCvr,
            request.PayerContactType is not null ? MapContactTypeToDb(request.PayerContactType) : null,
            request.PayerEmail, request.PayerPhone,
            request.PayerBillingStreet, request.PayerBillingHouseNumber, request.PayerBillingFloor,
            request.PayerBillingDoor, request.PayerBillingPostalCode, request.PayerBillingCity);
        var signupNumber = await _signupRepo.NextSignupNumberAsync(ct);
        var signup = await _signupRepo.CreateAsync(
            signupNumber, request.DarId ?? "", gsrn,
            request.CustomerName, request.CprCvr, dbContactType,
            request.ProductId, process.Id, request.Type, request.EffectiveDate,
            request.CorrectedFromId, addressInfo, request.Mobile, billingFrequency, paymentModel, ct);

        _logger.LogInformation(
            "Signup {SignupNumber} created for GSRN {Gsrn}, type={Type}, effective={EffectiveDate}{Correction}",
            signup.SignupNumber, gsrn, request.Type, request.EffectiveDate,
            request.CorrectedFromId.HasValue ? $", correcting {request.CorrectedFromId}" : "");

        return new SignupResponse(signup.Id, signup.SignupNumber, signup.Status, gsrn, request.EffectiveDate);
    }

    public async Task<SignupStatusResponse?> GetStatusAsync(string signupNumber, CancellationToken ct)
    {
        var signup = await _signupRepo.GetBySignupNumberAsync(signupNumber, ct);
        if (signup is null) return null;

        return new SignupStatusResponse(
            signup.SignupNumber, signup.Status, signup.Gsrn,
            signup.EffectiveDate, signup.RejectionReason);
    }

    public async Task CancelAsync(string signupNumber, CancellationToken ct)
    {
        var signup = await _signupRepo.GetBySignupNumberAsync(signupNumber, ct)
            ?? throw new ValidationException($"Signup {signupNumber} not found.");

        switch (signup.Status)
        {
            case "registered":
                // Not yet sent — cancel internally
                if (signup.ProcessRequestId.HasValue)
                {
                    var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                    await stateMachine.MarkCancelledAsync(signup.ProcessRequestId.Value, "Cancelled by user", ct);
                }
                await _signupRepo.UpdateStatusAsync(signup.Id, "cancelled", null, ct);
                _logger.LogInformation("Signup {SignupNumber} cancelled (was registered)", signupNumber);
                break;

            case "processing":
            case "awaiting_effectuation":
                // Already sent to DataHub — send BRS-003 cancellation, await DataHub acknowledgement
                if (signup.ProcessRequestId.HasValue)
                {
                    var process = await _processRepo.GetAsync(signup.ProcessRequestId.Value, ct);
                    if (process?.DatahubCorrelationId is not null)
                    {
                        var cimPayload = _brsBuilder.BuildBrs003(process.Gsrn, process.DatahubCorrelationId);
                        var response = await _dataHubClient.SendRequestAsync("cancel_switch", cimPayload, ct);
                        await _messageRepo.RecordOutboundRequestAsync(
                            "RSM-024", process.Gsrn, process.DatahubCorrelationId,
                            response.Accepted ? "acknowledged_ok" : "acknowledged_error", cimPayload, ct);
                        _logger.LogInformation(
                            "Sent BRS-003 cancel to DataHub for GSRN {Gsrn}, correlation={CorrelationId}",
                            process.Gsrn, process.DatahubCorrelationId);

                        var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                        await stateMachine.MarkCancellationSentAsync(signup.ProcessRequestId.Value, ct);
                    }
                    else
                    {
                        // No correlation ID — cancel locally without DataHub interaction
                        var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                        await stateMachine.MarkCancelledAsync(signup.ProcessRequestId.Value, "Cancelled by user", ct);
                    }
                }
                await _signupRepo.UpdateStatusAsync(signup.Id, "cancellation_pending", null, ct);
                _logger.LogInformation("Signup {SignupNumber} cancellation sent to DataHub (was {Status})", signupNumber, signup.Status);
                break;

            case "active":
                throw new ConflictException("Cannot cancel an active signup. Use offboarding instead.");

            default:
                throw new ValidationException($"Signup {signupNumber} is already {signup.Status}.");
        }
    }

    public async Task SyncFromProcessAsync(Guid processRequestId, string processStatus, string? reason, CancellationToken ct)
    {
        var signup = await _signupRepo.GetByProcessRequestIdAsync(processRequestId, ct);
        if (signup is null) return; // Process not linked to a signup (e.g., pre-onboarding processes)

        var newStatus = MapProcessStatusToSignupStatus(processStatus);
        if (newStatus is null || newStatus == signup.Status) return;

        // Create or link customer when signup becomes active
        if (newStatus == "active" && !signup.CustomerId.HasValue)
        {
            var signupDetail = await _signupRepo.GetDetailByIdAsync(signup.Id, ct);
            if (signupDetail is not null && !string.IsNullOrEmpty(signupDetail.CprCvr))
            {
                // Retrieve address/payer info captured at signup
                var addressInfo = await _signupRepo.GetAddressInfoAsync(signup.Id, ct);
                var billingAddress = addressInfo is not null
                    && (addressInfo.BillingStreet is not null || addressInfo.BillingPostalCode is not null || addressInfo.BillingCity is not null)
                    ? new Address(addressInfo.BillingStreet, addressInfo.BillingHouseNumber,
                        addressInfo.BillingFloor, addressInfo.BillingDoor,
                        addressInfo.BillingPostalCode, addressInfo.BillingCity,
                        addressInfo.BillingDarId)
                    : null;

                // Check if customer with this CPR/CVR already exists (multi-metering point scenario)
                var existingCustomer = await _portfolioRepo.GetCustomerByCprCvrAsync(signupDetail.CprCvr, ct);

                if (existingCustomer is not null)
                {
                    // Link to existing customer (e.g., home + summer residence)
                    await _signupRepo.LinkCustomerAsync(signup.Id, existingCustomer.Id, ct);

                    // Update billing address if provided and customer doesn't have one yet
                    if (billingAddress is not null && existingCustomer.BillingAddress is null)
                    {
                        await _portfolioRepo.UpdateCustomerBillingAddressAsync(existingCustomer.Id, billingAddress, ct);
                    }

                    _logger.LogInformation(
                        "Signup {SignupNumber} linked to existing customer {CustomerId} ({CustomerName}) — multi-metering point scenario",
                        signup.SignupNumber, existingCustomer.Id, existingCustomer.Name);
                }
                else
                {
                    // Create new customer with billing address
                    var customerContactType = MapContactType(signupDetail.ContactType);
                    var customer = await _portfolioRepo.CreateCustomerAsync(
                        signupDetail.CustomerName, signupDetail.CprCvr, customerContactType, billingAddress, ct);

                    await _signupRepo.LinkCustomerAsync(signup.Id, customer.Id, ct);

                    _logger.LogInformation("Customer {CustomerId} created for signup {SignupNumber}",
                        customer.Id, signup.SignupNumber);
                }

                // Create payer if a separate payer was specified at signup
                if (addressInfo is not null && !string.IsNullOrEmpty(addressInfo.PayerName))
                {
                    var payerContactType = MapContactType(addressInfo.PayerContactType ?? "person");
                    var payerAddress = addressInfo.PayerBillingStreet is not null
                        || addressInfo.PayerBillingPostalCode is not null
                        || addressInfo.PayerBillingCity is not null
                        ? new Address(addressInfo.PayerBillingStreet, addressInfo.PayerBillingHouseNumber,
                            addressInfo.PayerBillingFloor, addressInfo.PayerBillingDoor,
                            addressInfo.PayerBillingPostalCode, addressInfo.PayerBillingCity)
                        : null;  // Payer DAR ID not captured in signup flow — address from form fields only

                    var payer = await _portfolioRepo.CreatePayerAsync(
                        addressInfo.PayerName, addressInfo.PayerCprCvr ?? "",
                        payerContactType, addressInfo.PayerEmail, addressInfo.PayerPhone,
                        payerAddress, ct);

                    _logger.LogInformation("Payer {PayerId} ({PayerName}) created for signup {SignupNumber}",
                        payer.Id, payer.Name, signup.SignupNumber);
                }
            }
        }

        await _signupRepo.UpdateStatusAsync(signup.Id, newStatus, reason, ct);

        _logger.LogInformation("Signup {SignupNumber} status synced: {OldStatus} → {NewStatus} (process {ProcessStatus})",
            signup.SignupNumber, signup.Status, newStatus, processStatus);
    }

    private static string? MapProcessStatusToSignupStatus(string processStatus)
    {
        return processStatus switch
        {
            "pending" => "registered",
            "sent_to_datahub" or "acknowledged" => "processing",
            "effectuation_pending" => "awaiting_effectuation",
            "cancellation_pending" => "cancellation_pending",
            "completed" => "active",
            "rejected" => "rejected",
            "cancelled" => "cancelled",
            _ => null,
        };
    }

    private static string MapContactType(string signupContactType)
    {
        // Map signup.customer_contact_type ('person'/'company') to customer.contact_type ('private'/'business')
        return signupContactType switch
        {
            "person" => "private",
            "company" => "business",
            _ => signupContactType, // Fallback to original value
        };
    }

    private static string MapContactTypeToDb(string contactType)
    {
        // Map API contact type ('private'/'business') to DB constraint ('person'/'company')
        return contactType switch
        {
            "private" => "person",
            "business" => "company",
            _ => contactType,
        };
    }

    private void ValidateEffectiveDate(string type, DateOnly effectiveDate)
    {
        var today = _clock.Today;

        if (type == "move_in")
        {
            // BRS-009: earliest 60 days before, latest 7 days after effective date
            var earliest = today.AddDays(-7);
            var latest = today.AddDays(60);

            if (effectiveDate < earliest)
                throw new ValidationException(
                    $"Move-in effective date cannot be more than 7 days in the past. Earliest date: {earliest:yyyy-MM-dd}");

            if (effectiveDate > latest)
                throw new ValidationException(
                    $"Move-in effective date cannot be more than 60 days in the future. Latest date: {latest:yyyy-MM-dd}");
        }
        else // switch
        {
            // BRS-001: submit at latest the day before effective date, max 1 year in advance
            if (effectiveDate <= today)
                throw new ValidationException(
                    $"Supplier switch effective date must be after today. Earliest date: {today.AddDays(1):yyyy-MM-dd}");

            var maxDate = today.AddYears(1);
            if (effectiveDate > maxDate)
                throw new ValidationException(
                    $"Supplier switch cannot be more than 1 year in advance. Latest date: {maxDate:yyyy-MM-dd}");
        }
    }
}

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
