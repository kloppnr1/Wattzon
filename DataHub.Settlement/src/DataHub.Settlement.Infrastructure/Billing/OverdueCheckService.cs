using DataHub.Settlement.Application.Billing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Billing;

public sealed class OverdueCheckService : BackgroundService
{
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly ILogger<OverdueCheckService> _logger;
    private readonly TimeSpan _checkInterval;

    public OverdueCheckService(
        IInvoiceRepository invoiceRepo,
        ILogger<OverdueCheckService> logger,
        TimeSpan? checkInterval = null)
    {
        _invoiceRepo = invoiceRepo;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromHours(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Overdue check service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var overdueInvoices = await _invoiceRepo.GetSentPastDueDateAsync(today, stoppingToken);

                foreach (var invoice in overdueInvoices)
                {
                    await _invoiceRepo.UpdateStatusAsync(invoice.Id, "overdue", stoppingToken);
                    _logger.LogWarning(
                        "Invoice {InvoiceId} ({InvoiceNumber}) marked overdue — due {DueDate}, customer {CustomerId}",
                        invoice.Id, invoice.InvoiceNumber, invoice.DueDate, invoice.CustomerId);
                }

                if (overdueInvoices.Count > 0)
                {
                    _logger.LogInformation("Marked {Count} invoices as overdue", overdueInvoices.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during overdue check");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }
}
