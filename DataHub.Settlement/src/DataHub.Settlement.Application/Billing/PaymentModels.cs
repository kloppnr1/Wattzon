namespace DataHub.Settlement.Application.Billing;

public record Payment(
    Guid Id,
    Guid CustomerId,
    string PaymentMethod,
    string? PaymentReference,
    string? ExternalId,
    decimal Amount,
    decimal AmountAllocated,
    decimal AmountUnallocated,
    DateTime ReceivedAt,
    DateOnly? ValueDate,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record PaymentAllocation(
    Guid Id,
    Guid PaymentId,
    Guid InvoiceId,
    decimal Amount,
    DateTime AllocatedAt,
    string? AllocatedBy);

public record PaymentDetail(
    Payment Payment,
    IReadOnlyList<PaymentAllocation> Allocations,
    string? CustomerName);

public record PaymentSummary(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string PaymentMethod,
    string? PaymentReference,
    decimal Amount,
    decimal AmountAllocated,
    decimal AmountUnallocated,
    string Status,
    DateTime ReceivedAt);

public record CreatePaymentRequest(
    Guid CustomerId,
    string PaymentMethod,
    string? PaymentReference,
    string? ExternalId,
    decimal Amount,
    DateOnly? ValueDate);

public record ManualAllocationRequest(
    Guid InvoiceId,
    decimal Amount);

public record BankFilePayment(
    string PaymentReference,
    string? ExternalId,
    decimal Amount,
    DateOnly ValueDate);

public record BankFileImportRequest(
    IReadOnlyList<BankFilePayment> Payments);

public record BankFileImportResult(
    int TotalPayments,
    int Matched,
    int Unmatched,
    IReadOnlyList<string> Errors);
