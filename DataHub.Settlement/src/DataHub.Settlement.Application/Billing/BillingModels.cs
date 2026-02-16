namespace DataHub.Settlement.Application.Billing;

public record BillingPeriodSummary(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Frequency,
    int SettlementRunCount,
    DateTime CreatedAt);

public record BillingPeriodDetail(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Frequency,
    DateTime CreatedAt,
    IReadOnlyList<SettlementRunSummary> SettlementRuns);

public record SettlementRunSummary(
    Guid Id,
    Guid BillingPeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string GridAreaCode,
    int Version,
    string Status,
    DateTime ExecutedAt,
    DateTime? CompletedAt,
    string MeteringPointId,
    Guid? CustomerId);

public record SettlementRunDetail(
    Guid Id,
    Guid BillingPeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string GridAreaCode,
    int Version,
    string Status,
    DateTime ExecutedAt,
    DateTime? CompletedAt,
    string MeteringPointId,
    Guid? CustomerId,
    decimal TotalAmount,
    decimal TotalVat,
    string? ErrorDetails);

public record SettlementLineSummary(
    Guid Id,
    Guid SettlementRunId,
    string MeteringPointGsrn,
    string ChargeType,
    decimal TotalKwh,
    decimal TotalAmount,
    decimal VatAmount,
    string Currency);

public record SettlementLineDetail(
    Guid Id,
    Guid SettlementRunId,
    string MeteringPointGsrn,
    string CustomerName,
    string ChargeType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalKwh,
    decimal TotalAmount,
    decimal VatAmount,
    string Currency);

public record CustomerBillingSummary(
    Guid CustomerId,
    string CustomerName,
    IReadOnlyList<CustomerBillingPeriod> BillingPeriods,
    IReadOnlyList<AcontoPaymentInfo> AcontoPayments,
    decimal TotalBilled,
    decimal TotalPaid);

public record CustomerBillingPeriod(
    Guid BillingPeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalAmount,
    decimal TotalVat,
    IReadOnlyList<string> GsrnList);

public record AcontoPaymentInfo(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Amount,
    string Currency);
