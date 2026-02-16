using DataHub.Settlement.Application.Billing;

namespace DataHub.Settlement.UnitTests;

public sealed class NullInvoiceService : IInvoiceService
{
    public Task<Invoice> CreateInvoiceAsync(Guid customerId, Guid? payerId, Guid? contractId,
        Guid? settlementRunId, Guid? billingPeriodId, string gsrn,
        DateOnly periodStart, DateOnly periodEnd,
        IReadOnlyList<CreateInvoiceLineRequest> lines,
        DateOnly? dueDate, CancellationToken ct)
    {
        return Task.FromResult(new Invoice(
            Guid.NewGuid(), null, customerId, payerId, contractId, settlementRunId, billingPeriodId,
            "invoice", "draft", periodStart, periodEnd,
            0, 0, 0, 0, 0,
            null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow));
    }

    public Task<string> SendInvoiceAsync(Guid invoiceId, CancellationToken ct)
        => Task.FromResult("INV-0000-00000");

    public Task CancelInvoiceAsync(Guid invoiceId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<Invoice> CreateCreditNoteAsync(Guid invoiceId, string? notes, CancellationToken ct)
    {
        return Task.FromResult(new Invoice(
            Guid.NewGuid(), null, Guid.Empty, null, null, null, null,
            "credit_note", "draft", DateOnly.MinValue, DateOnly.MaxValue,
            0, 0, 0, 0, 0,
            null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow));
    }
}
