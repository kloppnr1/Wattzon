using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Common;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Billing;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly string _connectionString;

    static PaymentRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public PaymentRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<Payment> CreateAsync(CreatePaymentRequest request, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO billing.payment (customer_id, payment_method, payment_reference, external_id, amount, amount_unallocated, value_date)
            VALUES (@CustomerId, @PaymentMethod, @PaymentReference, @ExternalId, @Amount, @Amount, @ValueDate)
            RETURNING *
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<Payment>(
            new CommandDefinition(sql,
                new { request.CustomerId, request.PaymentMethod, request.PaymentReference,
                      request.ExternalId, request.Amount, request.ValueDate },
                cancellationToken: ct));
    }

    public async Task<Payment?> GetAsync(Guid id, CancellationToken ct)
    {
        const string sql = "SELECT * FROM billing.payment WHERE id = @Id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Payment>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<PaymentDetail?> GetDetailAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var payment = await conn.QuerySingleOrDefaultAsync<Payment>(
            new CommandDefinition("SELECT * FROM billing.payment WHERE id = @Id",
                new { Id = id }, cancellationToken: ct));
        if (payment is null) return null;

        var allocations = await conn.QueryAsync<PaymentAllocation>(
            new CommandDefinition(
                "SELECT * FROM billing.payment_allocation WHERE payment_id = @Id ORDER BY allocated_at",
                new { Id = id }, cancellationToken: ct));

        var customerName = await conn.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition("SELECT name FROM portfolio.customer WHERE id = @Id",
                new { Id = payment.CustomerId }, cancellationToken: ct));

        return new PaymentDetail(payment, allocations.ToList(), customerName);
    }

    public async Task<PagedResult<PaymentSummary>> GetPagedAsync(
        Guid? customerId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var conditions = new List<string>();
        if (customerId.HasValue) conditions.Add("p.customer_id = @CustomerId");
        if (!string.IsNullOrEmpty(status)) conditions.Add("p.status = @Status");

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var sql = $"""
            SELECT p.id, p.customer_id, c.name AS customer_name,
                   p.payment_method, p.payment_reference, p.amount,
                   p.amount_allocated, p.amount_unallocated, p.status, p.received_at,
                   COUNT(*) OVER() AS total_count
            FROM billing.payment p
            JOIN portfolio.customer c ON c.id = p.customer_id
            {where}
            ORDER BY p.received_at DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = (await conn.QueryAsync<PaymentSummaryRow>(
            new CommandDefinition(sql,
                new { CustomerId = customerId, Status = status,
                      Offset = (page - 1) * pageSize, PageSize = pageSize },
                cancellationToken: ct))).ToList();

        var totalCount = rows.FirstOrDefault()?.TotalCount ?? 0;
        var items = rows.Select(r => new PaymentSummary(
            r.Id, r.CustomerId, r.CustomerName, r.PaymentMethod,
            r.PaymentReference, r.Amount, r.AmountAllocated, r.AmountUnallocated,
            r.Status, r.ReceivedAt)).ToList();

        return new PagedResult<PaymentSummary>(items, totalCount, page, pageSize);
    }

    public async Task<PaymentAllocation> CreateAllocationAsync(
        Guid paymentId, Guid invoiceId, decimal amount, string? allocatedBy, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO billing.payment_allocation (payment_id, invoice_id, amount, allocated_by)
            VALUES (@PaymentId, @InvoiceId, @Amount, @AllocatedBy)
            RETURNING *
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<PaymentAllocation>(
            new CommandDefinition(sql,
                new { PaymentId = paymentId, InvoiceId = invoiceId, Amount = amount, AllocatedBy = allocatedBy },
                cancellationToken: ct));
    }

    public async Task UpdatePaymentAmountsAsync(Guid id, decimal amountAllocated, decimal amountUnallocated, string status, CancellationToken ct)
    {
        const string sql = """
            UPDATE billing.payment
            SET amount_allocated = @AmountAllocated, amount_unallocated = @AmountUnallocated, status = @Status, updated_at = now()
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, AmountAllocated = amountAllocated, AmountUnallocated = amountUnallocated, Status = status },
            cancellationToken: ct));
    }

    public async Task<Payment?> FindByReferenceAsync(string paymentReference, CancellationToken ct)
    {
        const string sql = "SELECT * FROM billing.payment WHERE payment_reference = @Ref LIMIT 1";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Payment>(
            new CommandDefinition(sql, new { Ref = paymentReference }, cancellationToken: ct));
    }

    public async Task<Guid?> FindCustomerByPaymentReferenceAsync(string paymentReference, CancellationToken ct)
    {
        // Payment reference is expected to be the invoice number — look up the customer from the invoice
        const string sql = "SELECT customer_id FROM billing.invoice WHERE invoice_number = @Ref LIMIT 1";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(sql, new { Ref = paymentReference }, cancellationToken: ct));
    }

    internal class PaymentSummaryRow
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public string? PaymentReference { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountAllocated { get; set; }
        public decimal AmountUnallocated { get; set; }
        public string Status { get; set; } = "";
        public DateTime ReceivedAt { get; set; }
        public int TotalCount { get; set; }
    }
}
