using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Application.Billing;

public interface IPaymentMatchingService
{
    Task<Payment> RecordAndMatchPaymentAsync(CreatePaymentRequest request, CancellationToken ct);
    Task ManualAllocateAsync(Guid paymentId, Guid invoiceId, decimal amount, string allocatedBy, CancellationToken ct);
    Task<BankFileImportResult> ImportBankFileAsync(BankFileImportRequest request, CancellationToken ct);
}

public sealed class PaymentMatchingService : IPaymentMatchingService
{
    private readonly IPaymentRepository _paymentRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly ILogger<PaymentMatchingService> _logger;

    public PaymentMatchingService(
        IPaymentRepository paymentRepo,
        IInvoiceRepository invoiceRepo,
        ILogger<PaymentMatchingService> logger)
    {
        _paymentRepo = paymentRepo;
        _invoiceRepo = invoiceRepo;
        _logger = logger;
    }

    public async Task<Payment> RecordAndMatchPaymentAsync(CreatePaymentRequest request, CancellationToken ct)
    {
        var payment = await _paymentRepo.CreateAsync(request, ct);
        await AutoMatchAsync(payment, ct);

        // Re-read to get updated amounts
        return (await _paymentRepo.GetAsync(payment.Id, ct))!;
    }

    public async Task ManualAllocateAsync(Guid paymentId, Guid invoiceId, decimal amount, string allocatedBy, CancellationToken ct)
    {
        var payment = await _paymentRepo.GetAsync(paymentId, ct)
            ?? throw new InvalidOperationException($"Payment {paymentId} not found");

        if (amount > payment.AmountUnallocated)
            throw new InvalidOperationException($"Amount {amount} exceeds unallocated balance {payment.AmountUnallocated}");

        var invoice = await _invoiceRepo.GetAsync(invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found");

        if (amount > invoice.AmountOutstanding)
            throw new InvalidOperationException($"Amount {amount} exceeds invoice outstanding {invoice.AmountOutstanding}");

        await AllocateToInvoiceAsync(payment, invoice, amount, allocatedBy, ct);
    }

    public async Task<BankFileImportResult> ImportBankFileAsync(BankFileImportRequest request, CancellationToken ct)
    {
        var matched = 0;
        var unmatched = 0;
        var errors = new List<string>();

        foreach (var bankPayment in request.Payments)
        {
            try
            {
                // Try to find customer by payment reference (invoice number)
                var customerId = await _paymentRepo.FindCustomerByPaymentReferenceAsync(
                    bankPayment.PaymentReference, ct);

                if (customerId is null)
                {
                    unmatched++;
                    errors.Add($"No customer found for reference {bankPayment.PaymentReference}");
                    continue;
                }

                var paymentRequest = new CreatePaymentRequest(
                    customerId.Value, "bank_transfer", bankPayment.PaymentReference,
                    bankPayment.ExternalId, bankPayment.Amount, bankPayment.ValueDate);

                await RecordAndMatchPaymentAsync(paymentRequest, ct);
                matched++;
            }
            catch (Exception ex)
            {
                unmatched++;
                errors.Add($"Error processing payment ref {bankPayment.PaymentReference}: {ex.Message}");
                _logger.LogWarning(ex, "Bank file import: failed to process payment ref {Ref}", bankPayment.PaymentReference);
            }
        }

        _logger.LogInformation("Bank file import: {Total} payments, {Matched} matched, {Unmatched} unmatched",
            request.Payments.Count, matched, unmatched);

        return new BankFileImportResult(request.Payments.Count, matched, unmatched, errors);
    }

    private async Task AutoMatchAsync(Payment payment, CancellationToken ct)
    {
        var outstanding = await _invoiceRepo.GetOutstandingForCustomerAsync(payment.CustomerId, ct);
        if (outstanding.Count == 0)
        {
            _logger.LogInformation("Payment {PaymentId}: no outstanding invoices for customer {CustomerId}",
                payment.Id, payment.CustomerId);
            return;
        }

        var remaining = payment.Amount;

        foreach (var invoice in outstanding)
        {
            if (remaining <= 0) break;

            var allocAmount = Math.Min(remaining, invoice.AmountOutstanding);
            await AllocateToInvoiceAsync(payment, invoice, allocAmount, "auto", ct);
            remaining -= allocAmount;
        }
    }

    private async Task AllocateToInvoiceAsync(Payment payment, Invoice invoice, decimal amount, string? allocatedBy, CancellationToken ct)
    {
        await _paymentRepo.CreateAllocationAsync(payment.Id, invoice.Id, amount, allocatedBy, ct);

        // Update invoice
        var newPaid = invoice.AmountPaid + amount;
        var newOutstanding = invoice.AmountOutstanding - amount;
        await _invoiceRepo.UpdatePaymentAmountsAsync(invoice.Id, newPaid, newOutstanding, ct);

        if (newOutstanding <= 0)
        {
            await _invoiceRepo.MarkPaidAsync(invoice.Id, ct);
            _logger.LogInformation("Invoice {InvoiceId} fully paid", invoice.Id);
        }
        else
        {
            await _invoiceRepo.UpdateStatusAsync(invoice.Id, "partially_paid", ct);
        }

        // Update payment
        var currentPayment = (await _paymentRepo.GetAsync(payment.Id, ct))!;
        var newAllocated = currentPayment.AmountAllocated + amount;
        var newUnallocated = currentPayment.AmountUnallocated - amount;
        var paymentStatus = newUnallocated <= 0 ? "allocated" : "partially_allocated";
        await _paymentRepo.UpdatePaymentAmountsAsync(payment.Id, newAllocated, newUnallocated, paymentStatus, ct);

        _logger.LogInformation("Allocated {Amount} DKK from payment {PaymentId} to invoice {InvoiceId}",
            amount, payment.Id, invoice.Id);
    }
}
