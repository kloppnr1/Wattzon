using DataHub.Settlement.Application.Common;

namespace DataHub.Settlement.Application.Billing;

public interface IInvoiceRepository
{
    Task<Invoice> CreateAsync(CreateInvoiceRequest request, IReadOnlyList<CreateInvoiceLineRequest> lines, CancellationToken ct);
    Task<Invoice?> GetAsync(Guid id, CancellationToken ct);
    Task<InvoiceDetail?> GetDetailAsync(Guid id, CancellationToken ct);
    Task<PagedResult<InvoiceSummary>> GetPagedAsync(Guid? customerId, string? status, string? invoiceType, DateOnly? fromDate, DateOnly? toDate, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<InvoiceSummary>> GetOverdueAsync(CancellationToken ct);
    Task<string> AssignInvoiceNumberAsync(Guid id, CancellationToken ct);
    Task UpdateStatusAsync(Guid id, string status, CancellationToken ct);
    Task UpdatePaymentAmountsAsync(Guid id, decimal amountPaid, decimal amountOutstanding, CancellationToken ct);
    Task MarkPaidAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Invoice>> GetOutstandingForCustomerAsync(Guid customerId, CancellationToken ct);
    Task<IReadOnlyList<Invoice>> GetSentPastDueDateAsync(DateOnly asOf, CancellationToken ct);
    Task<CustomerBalance?> GetCustomerBalanceAsync(Guid customerId, CancellationToken ct);
    Task<IReadOnlyList<CustomerLedgerEntry>> GetCustomerLedgerAsync(Guid customerId, CancellationToken ct);
    Task<IReadOnlyList<OutstandingCustomer>> GetOutstandingCustomersAsync(CancellationToken ct);
}
