using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Common;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Billing;

public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly string _connectionString;

    static InvoiceRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public InvoiceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<Invoice> CreateAsync(CreateInvoiceRequest request, IReadOnlyList<CreateInvoiceLineRequest> lines, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var totalExVat = lines.Sum(l => l.AmountExVat);
        var vatAmount = lines.Sum(l => l.VatAmount);
        var totalInclVat = lines.Sum(l => l.AmountInclVat);

        var invoice = await conn.QuerySingleAsync<Invoice>(
            new CommandDefinition("""
                INSERT INTO billing.invoice (customer_id, payer_id, contract_id, settlement_run_id, billing_period_id,
                    invoice_type, period_start, period_end, total_ex_vat, vat_amount, total_incl_vat,
                    amount_outstanding, due_date, notes)
                VALUES (@CustomerId, @PayerId, @ContractId, @SettlementRunId, @BillingPeriodId,
                    @InvoiceType, @PeriodStart, @PeriodEnd, @TotalExVat, @VatAmount, @TotalInclVat,
                    @TotalInclVat, @DueDate, @Notes)
                RETURNING *
                """,
                new
                {
                    request.CustomerId, request.PayerId, request.ContractId,
                    request.SettlementRunId, request.BillingPeriodId,
                    request.InvoiceType, request.PeriodStart, request.PeriodEnd,
                    TotalExVat = totalExVat, VatAmount = vatAmount, TotalInclVat = totalInclVat,
                    request.DueDate, request.Notes,
                },
                transaction: tx, cancellationToken: ct));

        foreach (var line in lines)
        {
            await conn.ExecuteAsync(
                new CommandDefinition("""
                    INSERT INTO billing.invoice_line (invoice_id, settlement_line_id, gsrn, sort_order,
                        line_type, description, quantity, unit_price, amount_ex_vat, vat_amount, amount_incl_vat)
                    VALUES (@InvoiceId, @SettlementLineId, @Gsrn, @SortOrder,
                        @LineType, @Description, @Quantity, @UnitPrice, @AmountExVat, @VatAmount, @AmountInclVat)
                    """,
                    new
                    {
                        InvoiceId = invoice.Id,
                        line.SettlementLineId, line.Gsrn, line.SortOrder,
                        line.LineType, line.Description, line.Quantity, line.UnitPrice,
                        line.AmountExVat, line.VatAmount, line.AmountInclVat,
                    },
                    transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return invoice;
    }

    public async Task<Invoice?> GetAsync(Guid id, CancellationToken ct)
    {
        const string sql = "SELECT * FROM billing.invoice WHERE id = @Id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Invoice>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<InvoiceDetail?> GetDetailAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var invoice = await conn.QuerySingleOrDefaultAsync<Invoice>(
            new CommandDefinition(
                "SELECT * FROM billing.invoice WHERE id = @Id",
                new { Id = id }, cancellationToken: ct));

        if (invoice is null) return null;

        var lines = await conn.QueryAsync<InvoiceLine>(
            new CommandDefinition(
                "SELECT * FROM billing.invoice_line WHERE invoice_id = @Id ORDER BY sort_order",
                new { Id = id }, cancellationToken: ct));

        var names = await conn.QuerySingleOrDefaultAsync<(string? CustomerName, string? PayerName)>(
            new CommandDefinition("""
                SELECT c.name AS CustomerName, p.name AS PayerName
                FROM portfolio.customer c
                LEFT JOIN portfolio.payer p ON p.id = @PayerId
                WHERE c.id = @CustomerId
                """,
                new { invoice.CustomerId, invoice.PayerId }, cancellationToken: ct));

        return new InvoiceDetail(invoice, lines.ToList(), names.CustomerName, names.PayerName);
    }

    public async Task<PagedResult<InvoiceSummary>> GetPagedAsync(
        Guid? customerId, string? status, string? invoiceType,
        DateOnly? fromDate, DateOnly? toDate,
        int page, int pageSize, CancellationToken ct)
    {
        var conditions = new List<string>();
        if (customerId.HasValue) conditions.Add("i.customer_id = @CustomerId");
        if (!string.IsNullOrEmpty(status)) conditions.Add("i.status = @Status");
        if (!string.IsNullOrEmpty(invoiceType)) conditions.Add("i.invoice_type = @InvoiceType");
        if (fromDate.HasValue) conditions.Add("i.period_start >= @FromDate");
        if (toDate.HasValue) conditions.Add("i.period_end <= @ToDate");

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var sql = $"""
            SELECT i.id, i.invoice_number, i.customer_id, c.name AS customer_name,
                   i.invoice_type, i.status, i.period_start, i.period_end,
                   i.total_incl_vat, i.amount_outstanding, i.due_date, i.created_at,
                   COUNT(*) OVER() AS total_count
            FROM billing.invoice i
            JOIN portfolio.customer c ON c.id = i.customer_id
            {where}
            ORDER BY i.created_at DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = (await conn.QueryAsync<InvoiceSummaryRow>(
            new CommandDefinition(sql,
                new { CustomerId = customerId, Status = status, InvoiceType = invoiceType,
                      FromDate = fromDate, ToDate = toDate,
                      Offset = (page - 1) * pageSize, PageSize = pageSize },
                cancellationToken: ct))).ToList();

        var totalCount = rows.FirstOrDefault()?.TotalCount ?? 0;
        var items = rows.Select(r => new InvoiceSummary(
            r.Id, r.InvoiceNumber, r.CustomerId, r.CustomerName,
            r.InvoiceType, r.Status, r.PeriodStart, r.PeriodEnd,
            r.TotalInclVat, r.AmountOutstanding, r.DueDate, r.CreatedAt)).ToList();

        return new PagedResult<InvoiceSummary>(items, totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetOverdueAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT i.id, i.invoice_number, i.customer_id, c.name AS customer_name,
                   i.invoice_type, i.status, i.period_start, i.period_end,
                   i.total_incl_vat, i.amount_outstanding, i.due_date, i.created_at
            FROM billing.invoice i
            JOIN portfolio.customer c ON c.id = i.customer_id
            WHERE i.status = 'overdue'
            ORDER BY i.due_date ASC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<InvoiceSummary>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<string> AssignInvoiceNumberAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            UPDATE billing.invoice
            SET invoice_number = 'INV-' || EXTRACT(YEAR FROM now())::TEXT || '-' || LPAD(nextval('billing.invoice_number_seq')::TEXT, 5, '0'),
                issued_at = now(),
                updated_at = now()
            WHERE id = @Id
            RETURNING invoice_number
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<string>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task UpdateStatusAsync(Guid id, string status, CancellationToken ct)
    {
        const string sql = "UPDATE billing.invoice SET status = @Status, updated_at = now() WHERE id = @Id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Status = status }, cancellationToken: ct));
    }

    public async Task UpdatePaymentAmountsAsync(Guid id, decimal amountPaid, decimal amountOutstanding, CancellationToken ct)
    {
        const string sql = """
            UPDATE billing.invoice
            SET amount_paid = @AmountPaid, amount_outstanding = @AmountOutstanding, updated_at = now()
            WHERE id = @Id
            """;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, AmountPaid = amountPaid, AmountOutstanding = amountOutstanding },
            cancellationToken: ct));
    }

    public async Task MarkPaidAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            UPDATE billing.invoice
            SET status = 'paid', paid_at = now(), amount_outstanding = 0, updated_at = now()
            WHERE id = @Id
            """;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Invoice>> GetOutstandingForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        const string sql = """
            SELECT * FROM billing.invoice
            WHERE customer_id = @CustomerId AND amount_outstanding > 0 AND status IN ('sent', 'partially_paid', 'overdue')
            ORDER BY due_date ASC NULLS LAST
            """;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Invoice>(
            new CommandDefinition(sql, new { CustomerId = customerId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<Invoice>> GetSentPastDueDateAsync(DateOnly asOf, CancellationToken ct)
    {
        const string sql = """
            SELECT * FROM billing.invoice
            WHERE status = 'sent' AND due_date IS NOT NULL AND due_date < @AsOf
            """;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Invoice>(
            new CommandDefinition(sql, new { AsOf = asOf }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<CustomerBalance?> GetCustomerBalanceAsync(Guid customerId, CancellationToken ct)
    {
        const string sql = """
            SELECT c.id AS customer_id, c.name AS customer_name,
                   COALESCE(SUM(i.total_incl_vat) FILTER (WHERE i.status != 'cancelled' AND i.status != 'credited'), 0) AS total_invoiced,
                   COALESCE(SUM(i.amount_paid) FILTER (WHERE i.status != 'cancelled' AND i.status != 'credited'), 0) AS total_paid,
                   COALESCE(SUM(i.amount_outstanding) FILTER (WHERE i.status IN ('sent', 'partially_paid', 'overdue')), 0) AS total_outstanding,
                   COALESCE(SUM(i.amount_outstanding) FILTER (WHERE i.status = 'overdue'), 0) AS total_overdue,
                   COUNT(i.id) FILTER (WHERE i.status != 'cancelled' AND i.status != 'credited') AS invoice_count,
                   COUNT(i.id) FILTER (WHERE i.status = 'overdue') AS overdue_count
            FROM portfolio.customer c
            LEFT JOIN billing.invoice i ON i.customer_id = c.id
            WHERE c.id = @CustomerId
            GROUP BY c.id, c.name
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CustomerBalance>(
            new CommandDefinition(sql, new { CustomerId = customerId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CustomerLedgerEntry>> GetCustomerLedgerAsync(Guid customerId, CancellationToken ct)
    {
        const string sql = """
            SELECT date, entry_type, reference, debit, credit, running_balance, invoice_id, payment_id
            FROM (
                SELECT i.issued_at AS date, 'invoice' AS entry_type, i.invoice_number AS reference,
                       i.total_incl_vat AS debit, NULL::NUMERIC AS credit, NULL::NUMERIC AS running_balance,
                       i.id AS invoice_id, NULL::UUID AS payment_id
                FROM billing.invoice i
                WHERE i.customer_id = @CustomerId AND i.status NOT IN ('draft', 'cancelled')

                UNION ALL

                SELECT p.received_at AS date, 'payment' AS entry_type, p.payment_reference AS reference,
                       NULL::NUMERIC AS debit, p.amount AS credit, NULL::NUMERIC AS running_balance,
                       NULL::UUID AS invoice_id, p.id AS payment_id
                FROM billing.payment p
                WHERE p.customer_id = @CustomerId
            ) ledger
            ORDER BY date ASC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<CustomerLedgerEntry>(
            new CommandDefinition(sql, new { CustomerId = customerId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<OutstandingCustomer>> GetOutstandingCustomersAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT c.id AS customer_id, c.name AS customer_name,
                   SUM(i.amount_outstanding) AS total_outstanding,
                   COALESCE(SUM(i.amount_outstanding) FILTER (WHERE i.status = 'overdue'), 0) AS total_overdue,
                   COUNT(i.id) AS outstanding_count,
                   COUNT(i.id) FILTER (WHERE i.status = 'overdue') AS overdue_count,
                   MIN(i.due_date) AS oldest_due_date
            FROM billing.invoice i
            JOIN portfolio.customer c ON c.id = i.customer_id
            WHERE i.status IN ('sent', 'partially_paid', 'overdue') AND i.amount_outstanding > 0
            GROUP BY c.id, c.name
            ORDER BY total_outstanding DESC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<OutstandingCustomer>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    internal class InvoiceSummaryRow
    {
        public Guid Id { get; set; }
        public string? InvoiceNumber { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = "";
        public string InvoiceType { get; set; } = "";
        public string Status { get; set; } = "";
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        public decimal TotalInclVat { get; set; }
        public decimal AmountOutstanding { get; set; }
        public DateOnly? DueDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalCount { get; set; }
    }
}
