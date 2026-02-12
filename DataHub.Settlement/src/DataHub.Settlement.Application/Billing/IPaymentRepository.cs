using DataHub.Settlement.Application.Common;

namespace DataHub.Settlement.Application.Billing;

public interface IPaymentRepository
{
    Task<Payment> CreateAsync(CreatePaymentRequest request, CancellationToken ct);
    Task<Payment?> GetAsync(Guid id, CancellationToken ct);
    Task<PaymentDetail?> GetDetailAsync(Guid id, CancellationToken ct);
    Task<PagedResult<PaymentSummary>> GetPagedAsync(Guid? customerId, string? status, int page, int pageSize, CancellationToken ct);
    Task<PaymentAllocation> CreateAllocationAsync(Guid paymentId, Guid invoiceId, decimal amount, string? allocatedBy, CancellationToken ct);
    Task UpdatePaymentAmountsAsync(Guid id, decimal amountAllocated, decimal amountUnallocated, string status, CancellationToken ct);
    Task<Payment?> FindByReferenceAsync(string paymentReference, CancellationToken ct);
    Task<Guid?> FindCustomerByPaymentReferenceAsync(string paymentReference, CancellationToken ct);
}
