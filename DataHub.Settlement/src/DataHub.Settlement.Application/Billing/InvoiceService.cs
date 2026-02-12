using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Application.Billing;

public interface IInvoiceService
{
    Task<Invoice> CreateAcontoInvoiceAsync(Guid customerId, Guid? payerId, Guid? contractId, string gsrn,
        DateOnly periodStart, DateOnly periodEnd, decimal amount, CancellationToken ct);
    Task<Invoice> CreateSettlementInvoiceAsync(Guid customerId, Guid? payerId, Guid? contractId,
        Guid settlementRunId, Guid billingPeriodId, string gsrn,
        DateOnly periodStart, DateOnly periodEnd,
        IReadOnlyList<CreateInvoiceLineRequest> lines, CancellationToken ct);
    Task<string> SendInvoiceAsync(Guid invoiceId, CancellationToken ct);
    Task CancelInvoiceAsync(Guid invoiceId, CancellationToken ct);
    Task<Invoice> CreateCreditNoteAsync(Guid invoiceId, string? notes, CancellationToken ct);
}

public sealed class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly IAcontoPaymentRepository _acontoRepo;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        IInvoiceRepository invoiceRepo,
        IAcontoPaymentRepository acontoRepo,
        ILogger<InvoiceService> logger)
    {
        _invoiceRepo = invoiceRepo;
        _acontoRepo = acontoRepo;
        _logger = logger;
    }

    public async Task<Invoice> CreateAcontoInvoiceAsync(
        Guid customerId, Guid? payerId, Guid? contractId, string gsrn,
        DateOnly periodStart, DateOnly periodEnd, decimal amount, CancellationToken ct)
    {
        var vatAmount = Math.Round(amount * 0.25m, 2);
        var totalInclVat = amount + vatAmount;
        var dueDate = periodStart.AddDays(14);

        var request = new CreateInvoiceRequest(
            customerId, payerId, contractId, null, null,
            "aconto", periodStart, periodEnd, dueDate, null);

        var lines = new List<CreateInvoiceLineRequest>
        {
            new(null, gsrn, 1, "aconto_charge", $"Aconto payment {periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}",
                null, null, amount, vatAmount, totalInclVat),
        };

        var invoice = await _invoiceRepo.CreateAsync(request, lines, ct);

        // Dual-write: also record in legacy aconto_payment table
        await _acontoRepo.RecordPaymentAsync(gsrn, periodStart, periodEnd, amount, ct);

        // Auto-send aconto invoices
        await SendInvoiceAsync(invoice.Id, ct);

        _logger.LogInformation(
            "Created aconto invoice {InvoiceId} for customer {CustomerId}, GSRN {Gsrn}, amount {Amount} DKK",
            invoice.Id, customerId, gsrn, totalInclVat);

        return invoice;
    }

    public async Task<Invoice> CreateSettlementInvoiceAsync(
        Guid customerId, Guid? payerId, Guid? contractId,
        Guid settlementRunId, Guid billingPeriodId, string gsrn,
        DateOnly periodStart, DateOnly periodEnd,
        IReadOnlyList<CreateInvoiceLineRequest> lines, CancellationToken ct)
    {
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        var request = new CreateInvoiceRequest(
            customerId, payerId, contractId, settlementRunId, billingPeriodId,
            "settlement", periodStart, periodEnd, dueDate, null);

        var invoice = await _invoiceRepo.CreateAsync(request, lines, ct);

        _logger.LogInformation(
            "Created settlement invoice {InvoiceId} for customer {CustomerId}, GSRN {Gsrn}, amount {Amount} DKK",
            invoice.Id, customerId, gsrn, invoice.TotalInclVat);

        return invoice;
    }

    public async Task<string> SendInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _invoiceRepo.GetAsync(invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found");

        if (invoice.Status != "draft")
            throw new InvalidOperationException($"Invoice {invoiceId} is {invoice.Status}, cannot send");

        var invoiceNumber = await _invoiceRepo.AssignInvoiceNumberAsync(invoiceId, ct);
        await _invoiceRepo.UpdateStatusAsync(invoiceId, "sent", ct);

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
