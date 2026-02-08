using DataHub.Settlement.Application.Portfolio;

namespace DataHub.Settlement.Application.Onboarding;

public interface ISignupRepository
{
    Task<Signup> CreateAsync(string signupNumber, string darId, string gsrn, Guid customerId,
        Guid productId, Guid processRequestId, string type, DateOnly effectiveDate, CancellationToken ct);
    Task<string> NextSignupNumberAsync(CancellationToken ct);
    Task<Signup?> GetBySignupNumberAsync(string signupNumber, CancellationToken ct);
    Task<Signup?> GetByProcessRequestIdAsync(Guid processRequestId, CancellationToken ct);
    Task<Signup?> GetActiveByGsrnAsync(string gsrn, CancellationToken ct);
    Task UpdateStatusAsync(Guid id, string status, string? rejectionReason, CancellationToken ct);
    Task SetProcessRequestIdAsync(Guid id, Guid processRequestId, CancellationToken ct);
    Task<string?> GetCustomerCprCvrAsync(Guid signupId, CancellationToken ct);
    Task<IReadOnlyList<SignupListItem>> GetAllAsync(string? statusFilter, CancellationToken ct);
    Task<PagedResult<SignupListItem>> GetAllPagedAsync(string? statusFilter, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<SignupListItem>> GetRecentAsync(int limit, CancellationToken ct);
    Task<SignupDetail?> GetDetailByIdAsync(Guid id, CancellationToken ct);
}
