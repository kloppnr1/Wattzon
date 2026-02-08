using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Domain;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Onboarding;

public sealed class OnboardingService : IOnboardingService
{
    private const int SwitchNoticeBusinessDays = 15;

    private readonly ISignupRepository _signupRepo;
    private readonly IPortfolioRepository _portfolioRepo;
    private readonly IProcessRepository _processRepo;
    private readonly IAddressLookupClient _addressLookup;
    private readonly IClock _clock;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        ISignupRepository signupRepo,
        IPortfolioRepository portfolioRepo,
        IProcessRepository processRepo,
        IAddressLookupClient addressLookup,
        IClock clock,
        ILogger<OnboardingService> logger)
    {
        _signupRepo = signupRepo;
        _portfolioRepo = portfolioRepo;
        _processRepo = processRepo;
        _addressLookup = addressLookup;
        _clock = clock;
        _logger = logger;
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

        // 1. Resolve GSRN from DAR ID
        var lookupResult = await _addressLookup.LookupByDarIdAsync(request.DarId, ct);

        if (lookupResult.MeteringPoints.Count == 0)
            throw new ValidationException("No metering point found for the given address.");

        if (lookupResult.MeteringPoints.Count > 1)
            throw new ValidationException("Multiple metering points found for this address. Please resolve before submitting.");

        var gsrn = lookupResult.MeteringPoints[0].Gsrn;

        // 2. Validate GSRN format
        if (!GsrnValidator.IsValid(gsrn))
            throw new ValidationException($"Invalid GSRN format: {gsrn}");

        // 3. Check no active signup for this GSRN
        var existing = await _signupRepo.GetActiveByGsrnAsync(gsrn, ct);
        if (existing is not null)
            throw new ValidationException($"An active signup already exists for GSRN {gsrn} ({existing.SignupNumber}).");

        // 4. Validate product
        var product = await _portfolioRepo.GetProductAsync(request.ProductId, ct)
            ?? throw new ValidationException($"Product {request.ProductId} not found.");

        // 5. Validate type
        if (request.Type is not ("switch" or "move_in"))
            throw new ValidationException($"Invalid type '{request.Type}'. Must be 'switch' or 'move_in'.");

        // 6. Validate effective date
        ValidateEffectiveDate(request.Type, request.EffectiveDate);

        // 7. Map type to process type
        var processType = request.Type == "switch" ? "supplier_switch" : "move_in";

        // 8. Create customer
        var customer = await _portfolioRepo.CreateCustomerAsync(
            request.CustomerName, request.CprCvr, request.ContactType, ct);

        // 9. Create process request
        var stateMachine = new ProcessStateMachine(_processRepo, _clock);
        var process = await stateMachine.CreateRequestAsync(gsrn, processType, request.EffectiveDate, ct);

        // 10. Create signup (with optional correction link)
        var signupNumber = await _signupRepo.NextSignupNumberAsync(ct);
        var signup = await _signupRepo.CreateAsync(
            signupNumber, request.DarId, gsrn, customer.Id,
            request.ProductId, process.Id, request.Type, request.EffectiveDate,
            request.CorrectedFromId, ct);

        _logger.LogInformation(
            "Signup {SignupNumber} created for GSRN {Gsrn}, type={Type}, effective={EffectiveDate}{Correction}",
            signup.SignupNumber, gsrn, request.Type, request.EffectiveDate,
            request.CorrectedFromId.HasValue ? $", correcting {request.CorrectedFromId}" : "");

        return new SignupResponse(signup.SignupNumber, signup.Status, gsrn, request.EffectiveDate);
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
                // Already sent to DataHub — cancel process, status will sync
                if (signup.ProcessRequestId.HasValue)
                {
                    var stateMachine = new ProcessStateMachine(_processRepo, _clock);
                    await stateMachine.MarkCancelledAsync(signup.ProcessRequestId.Value, "Cancelled by user", ct);
                }
                await _signupRepo.UpdateStatusAsync(signup.Id, "cancelled", null, ct);
                _logger.LogInformation("Signup {SignupNumber} cancelled (was processing)", signupNumber);
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

        await _signupRepo.UpdateStatusAsync(signup.Id, newStatus, reason, ct);

        _logger.LogInformation("Signup {SignupNumber} status synced: {OldStatus} → {NewStatus} (process {ProcessStatus})",
            signup.SignupNumber, signup.Status, newStatus, processStatus);
    }

    private static string? MapProcessStatusToSignupStatus(string processStatus)
    {
        return processStatus switch
        {
            "pending" => "registered",
            "sent_to_datahub" or "acknowledged" or "effectuation_pending" => "processing",
            "completed" => "active",
            "rejected" => "rejected",
            "cancelled" => "cancelled",
            _ => null,
        };
    }

    private void ValidateEffectiveDate(string type, DateOnly effectiveDate)
    {
        var today = _clock.Today;

        if (type == "move_in")
        {
            if (effectiveDate < today)
                throw new ValidationException("Effective date cannot be in the past.");
        }
        else // switch
        {
            var earliest = BusinessDayCalculator.EarliestEffectiveDate(today, SwitchNoticeBusinessDays);
            if (effectiveDate < earliest)
                throw new ValidationException(
                    $"Supplier switch requires {SwitchNoticeBusinessDays} business days notice. Earliest date: {earliest:yyyy-MM-dd}");
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
