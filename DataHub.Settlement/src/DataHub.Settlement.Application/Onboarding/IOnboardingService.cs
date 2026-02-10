namespace DataHub.Settlement.Application.Onboarding;

public interface IOnboardingService
{
    Task<AddressLookupResponse> LookupAddressAsync(string darId, CancellationToken ct);
    Task<SignupResponse> CreateSignupAsync(SignupRequest request, CancellationToken ct);
    Task<SignupStatusResponse?> GetStatusAsync(string signupNumber, CancellationToken ct);
    Task CancelAsync(string signupNumber, CancellationToken ct);
    Task SyncFromProcessAsync(Guid processRequestId, string processStatus, string? reason, CancellationToken ct);
}

public record AddressLookupResponse(IReadOnlyList<MeteringPointResponse> MeteringPoints);
public record MeteringPointResponse(string Gsrn, string Type, string GridAreaCode, bool HasActiveProcess);
