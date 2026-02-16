using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Application.Billing;

public interface IInvoiceService
{
    /// <summary>
    /// Creates a billing document (invoice) with the given line items.
    /// The line items determine what the invoice is about: settlement charges,
    /// aconto prepayment, aconto deduction, or any combination.
    /// </summary>
    Task<Invoice> CreateInvoiceAsync(Guid customerId, Guid? payerId, Guid? contractId,
        Guid? settlementRunId, Guid? billingPeriodId, string gsrn,
        DateOnly periodStart, DateOnly periodEnd,
        IReadOnlyList<CreateInvoiceLineRequest> lines,
        DateOnly? dueDate, CancellationToken ct);
    Task<string> SendInvoiceAsync(Guid invoiceId, CancellationToken ct);
    Task CancelInvoiceAsync(Guid invoiceId, CancellationToken ct);
    Task<Invoice> CreateCreditNoteAsync(Guid invoiceId, string? notes, CancellationToken ct);
}

public sealed class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        IInvoiceRepository invoiceRepo,
        ILogger<InvoiceService> logger)
    {
        _invoiceRepo = invoiceRepo;
        _logger = logger;
    }

    public async Task<Invoice> CreateInvoiceAsync(
        Guid customerId, Guid? payerId, Guid? contractId,
        Guid? settlementRunId, Guid? billingPeriodId, string gsrn,
        DateOnly periodStart, DateOnly periodEnd,
        IReadOnlyList<CreateInvoiceLineRequest> lines,
        DateOnly? dueDate, CancellationToken ct)
    {
        dueDate ??= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        var request = new CreateInvoiceRequest(
            customerId, payerId, contractId, settlementRunId, billingPeriodId,
            "invoice", periodStart, periodEnd, dueDate, null);

        var invoice = await _invoiceRepo.CreateAsync(request, lines, ct);

        // Auto-send
        await SendInvoiceAsync(invoice.Id, ct);

        _logger.LogInformation(
            "Created invoice {InvoiceId} for customer {CustomerId}, GSRN {Gsrn}, amount {Amount} DKK",
            invoice.Id, customerId, gsrn, invoice.TotalInclVat);

        return invoice;
    }

    public async Task<string> SendInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        // AssignInvoiceNumberAsync atomically sets invoice_number + status='sent' in a single UPDATE.
        // This prevents gaps in invoice numbering from partial failures.
        var invoiceNumber = await _invoiceRepo.AssignInvoiceNumberAsync(invoiceId, ct);

        _logger.LogInformation("Sent invoice {InvoiceNumber} ({InvoiceId})", invoiceNumber, invoiceId);
        return invoiceNumber;
    }

    public async Task CancelInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _invoiceRepo.GetAsync(invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found");

        if (invoice.Status is "paid" or "credited" or "cancelled")
            throw new InvalidOperationException($"Invoice {invoiceId} is {invoice.Status}, cannot cancel");

        await _invoiceRepo.UpdateStatusAsync(invoiceId, "cancelled", ct);
        _logger.LogInformation("Cancelled invoice {InvoiceId}", invoiceId);
    }

    public async Task<Invoice> CreateCreditNoteAsync(Guid invoiceId, string? notes, CancellationToken ct)
    {
        var original = await _invoiceRepo.GetDetailAsync(invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found");

        if (original.Invoice.Status is "cancelled" or "credited")
            throw new InvalidOperationException($"Invoice {invoiceId} is {original.Invoice.Status}, cannot credit");

        var creditLines = original.Lines.Select((l, i) => new CreateInvoiceLineRequest(
            l.SettlementLineId, l.Gsrn, i + 1, l.LineType,
            $"Credit: {l.Description}",
            l.Quantity, l.UnitPrice,
            -l.AmountExVat, -l.VatAmount, -l.AmountInclVat)).ToList();

        var request = new CreateInvoiceRequest(
            original.Invoice.CustomerId, original.Invoice.PayerId, original.Invoice.ContractId,
            original.Invoice.SettlementRunId, original.Invoice.BillingPeriodId,
            "credit_note", original.Invoice.PeriodStart, original.Invoice.PeriodEnd,
            null, notes ?? $"Credit note for {original.Invoice.InvoiceNumber}");

        var creditNote = await _invoiceRepo.CreateAsync(request, creditLines, ct);

        // Mark original as credited
        await _invoiceRepo.UpdateStatusAsync(invoiceId, "credited", ct);

        // Auto-send credit note
        await SendInvoiceAsync(creditNote.Id, ct);

        _logger.LogInformation("Created credit note {CreditId} for invoice {InvoiceId}", creditNote.Id, invoiceId);
        return creditNote;
    }
}
