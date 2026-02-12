namespace DataHub.Settlement.Application.Billing;

public record Invoice(
    Guid Id,
    string? InvoiceNumber,
    Guid CustomerId,
    Guid? PayerId,
    Guid? ContractId,
    Guid? SettlementRunId,
    Guid? BillingPeriodId,
    string InvoiceType,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalExVat,
    decimal VatAmount,
    decimal TotalInclVat,
    decimal AmountPaid,
    decimal AmountOutstanding,
    DateTime? IssuedAt,
    DateOnly? DueDate,
    DateTime? PaidAt,
    Guid? CreditedInvoiceId,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record InvoiceLine(
    Guid Id,
    Guid InvoiceId,
    Guid? SettlementLineId,
    string Gsrn,
    int SortOrder,
    string LineType,
    string Description,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal AmountExVat,
    decimal VatAmount,
    decimal AmountInclVat);

public record InvoiceDetail(
    Invoice Invoice,
    IReadOnlyList<InvoiceLine> Lines,
    string? CustomerName,
    string? PayerName);

public record InvoiceSummary(
    Guid Id,
    string? InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    string InvoiceType,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalInclVat,
    decimal AmountOutstanding,
    DateOnly? DueDate,
    DateTime CreatedAt);

public record CreateInvoiceRequest(
    Guid CustomerId,
    Guid? PayerId,
    Guid? ContractId,
    Guid? SettlementRunId,
    Guid? BillingPeriodId,
    string InvoiceType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? DueDate,
    string? Notes);

public record CreateInvoiceLineRequest(
    Guid? SettlementLineId,
    string Gsrn,
    int SortOrder,
    string LineType,
    string Description,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal AmountExVat,
    decimal VatAmount,
    decimal AmountInclVat);

public record CustomerBalance(
    Guid CustomerId,
    string CustomerName,
    decimal TotalInvoiced,
    decimal TotalPaid,
    decimal TotalOutstanding,
    decimal TotalOverdue,
    int InvoiceCount,
    int OverdueCount);

public record CustomerLedgerEntry(
    DateTime Date,
    string EntryType,
    string? Reference,
    decimal? Debit,
    decimal? Credit,
    decimal RunningBalance,
    Guid? InvoiceId,
    Guid? PaymentId);

public record OutstandingCustomer(
    Guid CustomerId,
    string CustomerName,
    decimal TotalOutstanding,
    decimal TotalOverdue,
    int OutstandingCount,
    int OverdueCount,
    DateOnly? OldestDueDate);
