namespace DataHub.Settlement.Application.Onboarding;

public interface IOnboardingService
{
    Task<SignupResponse> CreateSignupAsync(SignupRequest request, CancellationToken ct);
    Task<SignupStatusResponse?> GetStatusAsync(string signupNumber, CancellationToken ct);
    Task CancelAsync(string signupNumber, CancellationToken ct);
    Task SyncFromProcessAsync(Guid processRequestId, string processStatus, string? reason, CancellationToken ct);
}
