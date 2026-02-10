using DataHub.Settlement.Application.Onboarding;

namespace DataHub.Settlement.UnitTests;

/// <summary>
/// No-op stub implementation of IOnboardingService for use in integration tests
/// where the onboarding operations are not relevant to the test scenario.
/// </summary>
public sealed class NullOnboardingService : IOnboardingService
{
    public static readonly IOnboardingService Instance = new NullOnboardingService();

    public Task<AddressLookupResponse> LookupAddressAsync(string darId, CancellationToken ct)
    {
        return Task.FromResult(new AddressLookupResponse(Array.Empty<MeteringPointResponse>()));
    }

    public Task<SignupResponse> CreateSignupAsync(SignupRequest request, CancellationToken ct)
    {
        return Task.FromResult(new SignupResponse(
            SignupId: Guid.NewGuid().ToString(),
            Status: "created",
            Gsrn: "571313100000000000",
            EffectiveDate: request.EffectiveDate));
    }

    public Task<SignupStatusResponse?> GetStatusAsync(string signupNumber, CancellationToken ct)
    {
        return Task.FromResult<SignupStatusResponse?>(null);
    }

    public Task CancelAsync(string signupNumber, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task SyncFromProcessAsync(Guid processRequestId, string processStatus, string? reason, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
