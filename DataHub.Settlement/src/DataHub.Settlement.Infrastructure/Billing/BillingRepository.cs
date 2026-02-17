using Dapper;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Common;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Billing;

public sealed class BillingRepository : IBillingRepository
{
    private readonly string _connectionString;

    static BillingRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public BillingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<PagedResult<BillingPeriodSummary>> GetBillingPeriodsAsync(int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        const string sql = """
            WITH counted AS (
                SELECT bp.id, bp.period_start, bp.period_end, bp.frequency, bp.created_at,
                       COUNT(*) OVER() AS total_count
                FROM settlement.billing_period bp
                ORDER BY bp.period_start DESC
                LIMIT @PageSize OFFSET @Offset
            )
            SELECT c.id, c.period_start, c.period_end, c.frequency, c.created_at, c.total_count,
                   (SELECT COUNT(*) FROM settlement.settlement_run WHERE billing_period_id = c.id) AS settlement_run_count
            FROM counted c
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<BillingPeriodRow>(sql, new { PageSize = pageSize, Offset = offset });
        var rowList = rows.ToList();

        var totalCount = rowList.FirstOrDefault()?.TotalCount ?? 0;
        var items = rowList.Select(r => new BillingPeriodSummary(
            r.Id,
            r.PeriodStart,
            r.PeriodEnd,
            r.Frequency,
            r.SettlementRunCount,
            r.CreatedAt)).ToList();

        return new PagedResult<BillingPeriodSummary>(items, totalCount, page, pageSize);
    }

    public async Task<BillingPeriodDetail?> GetBillingPeriodAsync(Guid billingPeriodId, CancellationToken ct)
    {
        const string periodSql = """
            SELECT id, period_start, period_end, frequency, created_at
            FROM settlement.billing_period
            WHERE id = @Id
            """;

        const string runsSql = """
            SELECT sr.id, sr.billing_period_id, bp.period_start, bp.period_end,
                   sr.grid_area_code, sr.version, sr.status, sr.executed_at, sr.completed_at,
                   sr.metering_point_id, ct.customer_id
            FROM settlement.settlement_run sr
            JOIN settlement.billing_period bp ON sr.billing_period_id = bp.id
            LEFT JOIN portfolio.contract ct ON sr.metering_point_id = ct.gsrn
            WHERE sr.billing_period_id = @BillingPeriodId
            ORDER BY sr.executed_at DESC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var period = await conn.QuerySingleOrDefaultAsync<BillingPeriodRow>(periodSql, new { Id = billingPeriodId });
        if (period is null)
            return null;

        var runs = await conn.QueryAsync<SettlementRunRow>(runsSql, new { BillingPeriodId = billingPeriodId });
        var runList = runs.Select(r => new SettlementRunSummary(
            r.Id,
            r.BillingPeriodId,
            r.PeriodStart,
            r.PeriodEnd,
            r.GridAreaCode,
            r.Version,
            r.Status,
            r.ExecutedAt,
            r.CompletedAt,
            r.MeteringPointId,
            r.CustomerId)).ToList();

        return new BillingPeriodDetail(
            period.Id,
            period.PeriodStart,
            period.PeriodEnd,
            period.Frequency,
            period.CreatedAt,
            runList);
    }

    public async Task<PagedResult<SettlementRunSummary>> GetSettlementRunsAsync(Guid? billingPeriodId, string? status, string? meteringPointId, string? gridAreaCode, DateOnly? fromDate, DateOnly? toDate, int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        var sql = """
            WITH counted AS (
                SELECT sr.id, sr.billing_period_id, bp.period_start, bp.period_end,
                       sr.grid_area_code, sr.version, sr.status, sr.executed_at, sr.completed_at,
                       sr.metering_point_id, COUNT(*) OVER() AS total_count
                FROM settlement.settlement_run sr
                JOIN settlement.billing_period bp ON sr.billing_period_id = bp.id
                WHERE 1=1
            """;

        if (billingPeriodId.HasValue)
            sql += " AND sr.billing_period_id = @BillingPeriodId\n";
        if (!string.IsNullOrEmpty(status))
            sql += " AND sr.status = @Status\n";
        if (!string.IsNullOrEmpty(meteringPointId))
            sql += " AND sr.metering_point_id = @MeteringPointId\n";
        if (!string.IsNullOrEmpty(gridAreaCode))
            sql += " AND sr.grid_area_code = @GridAreaCode\n";
        if (fromDate.HasValue)
            sql += " AND bp.period_start >= @FromDate\n";
        if (toDate.HasValue)
            sql += " AND bp.period_end <= @ToDate\n";

        sql += """
                ORDER BY sr.executed_at DESC
                LIMIT @PageSize OFFSET @Offset
            )
            SELECT c.id, c.billing_period_id, c.period_start, c.period_end,
                   c.grid_area_code, c.version, c.status, c.executed_at, c.completed_at, c.total_count,
                   c.metering_point_id, ct.customer_id
            FROM counted c
            LEFT JOIN portfolio.contract ct ON c.metering_point_id = ct.gsrn
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<SettlementRunRow>(sql, new
        {
            BillingPeriodId = billingPeriodId,
            Status = status,
            MeteringPointId = meteringPointId,
            GridAreaCode = gridAreaCode,
            FromDate = fromDate,
            ToDate = toDate,
            PageSize = pageSize,
            Offset = offset
        });
        var rowList = rows.ToList();

        var totalCount = rowList.FirstOrDefault()?.TotalCount ?? 0;
        var items = rowList.Select(r => new SettlementRunSummary(
            r.Id,
            r.BillingPeriodId,
            r.PeriodStart,
            r.PeriodEnd,
            r.GridAreaCode,
            r.Version,
            r.Status,
            r.ExecutedAt,
            r.CompletedAt,
            r.MeteringPointId,
            r.CustomerId)).ToList();

        return new PagedResult<SettlementRunSummary>(items, totalCount, page, pageSize);
    }

    public async Task<SettlementRunDetail?> GetSettlementRunAsync(Guid settlementRunId, CancellationToken ct)
    {
        const string sql = """
            SELECT sr.id, sr.billing_period_id, bp.period_start, bp.period_end, sr.grid_area_code, sr.version, sr.status, sr.executed_at, sr.completed_at, sr.error_details,
                   sr.metering_point_id, ct.customer_id,
                   (SELECT COALESCE(SUM(total_amount), 0) FROM settlement.settlement_line WHERE settlement_run_id = sr.id) AS total_amount,
                   (SELECT COALESCE(SUM(vat_amount), 0) FROM settlement.settlement_line WHERE settlement_run_id = sr.id) AS total_vat
            FROM settlement.settlement_run sr
            JOIN settlement.billing_period bp ON sr.billing_period_id = bp.id
            LEFT JOIN portfolio.contract ct ON sr.metering_point_id = ct.gsrn
            WHERE sr.id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QuerySingleOrDefaultAsync<SettlementRunDetailRow>(sql, new { Id = settlementRunId });
        if (row is null)
            return null;

        return new SettlementRunDetail(
            row.Id,
            row.BillingPeriodId,
            row.PeriodStart,
            row.PeriodEnd,
            row.GridAreaCode,
            row.Version,
            row.Status,
            row.ExecutedAt,
            row.CompletedAt,
            row.MeteringPointId,
            row.CustomerId,
            row.TotalAmount,
            row.TotalVat,
            row.ErrorDetails);
    }

    public async Task<PagedResult<SettlementLineSummary>> GetSettlementLinesAsync(Guid settlementRunId, int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        const string sql = """
            SELECT sl.id, sl.settlement_run_id, sl.metering_point_id AS metering_point_gsrn, sl.charge_type, sl.total_kwh, sl.total_amount, sl.vat_amount, sl.currency,
                   COUNT(*) OVER() AS total_count
            FROM settlement.settlement_line sl
            WHERE sl.settlement_run_id = @SettlementRunId
            ORDER BY sl.metering_point_id, sl.charge_type
            LIMIT @PageSize OFFSET @Offset
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<SettlementLineSummaryRow>(sql, new { SettlementRunId = settlementRunId, PageSize = pageSize, Offset = offset });
        var rowList = rows.ToList();

        var totalCount = rowList.FirstOrDefault()?.TotalCount ?? 0;
        var items = rowList.Select(r => new SettlementLineSummary(
            r.Id,
            r.SettlementRunId,
            r.MeteringPointGsrn,
            r.ChargeType,
            r.TotalKwh,
            r.TotalAmount,
            r.VatAmount,
            r.Currency)).ToList();

        return new PagedResult<SettlementLineSummary>(items, totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<SettlementLineDetail>> GetSettlementLinesByMeteringPointAsync(string gsrn, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        var sql = """
            SELECT sl.id, sl.settlement_run_id, sl.metering_point_id AS metering_point_gsrn, c.name AS customer_name, sl.charge_type, bp.period_start, bp.period_end, sl.total_kwh, sl.total_amount, sl.vat_amount, sl.currency
            FROM settlement.settlement_line sl
            JOIN settlement.settlement_run sr ON sl.settlement_run_id = sr.id
            JOIN settlement.billing_period bp ON sr.billing_period_id = bp.id
            JOIN portfolio.contract ct ON sl.metering_point_id = ct.gsrn
            JOIN portfolio.customer c ON ct.customer_id = c.id
            WHERE sl.metering_point_id = @Gsrn
            """;

        if (fromDate.HasValue)
            sql += " AND bp.period_start >= @FromDate\n";
        if (toDate.HasValue)
            sql += " AND bp.period_end <= @ToDate\n";

        sql += " ORDER BY bp.period_start DESC, sl.charge_type";

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<SettlementLineDetailRow>(sql, new { Gsrn = gsrn, FromDate = fromDate, ToDate = toDate });

        return rows.Select(r => new SettlementLineDetail(
            r.Id,
            r.SettlementRunId,
            r.MeteringPointGsrn,
            r.CustomerName,
            r.ChargeType,
            r.PeriodStart,
            r.PeriodEnd,
            r.TotalKwh,
            r.TotalAmount,
            r.VatAmount,
            r.Currency)).ToList();
    }

    public async Task<CustomerBillingSummary?> GetCustomerBillingAsync(Guid customerId, CancellationToken ct)
    {
        const string customerSql = "SELECT name FROM portfolio.customer WHERE id = @Id";

        const string periodsSql = """
            SELECT DISTINCT bp.id AS billing_period_id, bp.period_start, bp.period_end,
                   SUM(sl.total_amount) AS total_amount,
                   SUM(sl.vat_amount) AS total_vat
            FROM settlement.settlement_line sl
            JOIN settlement.settlement_run sr ON sl.settlement_run_id = sr.id
            JOIN settlement.billing_period bp ON sr.billing_period_id = bp.id
            JOIN portfolio.contract ct ON sl.metering_point_id = ct.gsrn
            WHERE ct.customer_id = @CustomerId
            GROUP BY bp.id, bp.period_start, bp.period_end
            ORDER BY bp.period_start DESC
            """;

        const string gsrnSql = """
            SELECT DISTINCT sr.billing_period_id, sl.metering_point_id AS gsrn
            FROM settlement.settlement_line sl
            JOIN settlement.settlement_run sr ON sl.settlement_run_id = sr.id
            JOIN portfolio.contract ct ON sl.metering_point_id = ct.gsrn
            WHERE ct.customer_id = @CustomerId
            """;

        const string acontoSql = """
            SELECT i.id AS invoice_id, i.invoice_number, i.period_start, i.period_end,
                   il.amount_ex_vat AS amount, 'DKK' AS currency
            FROM billing.invoice_line il
            JOIN billing.invoice i ON i.id = il.invoice_id
            JOIN portfolio.contract ct ON ct.gsrn = il.gsrn
            WHERE ct.customer_id = @CustomerId
              AND il.line_type = 'aconto_prepayment'
              AND i.status NOT IN ('cancelled', 'credited')
            ORDER BY i.period_start DESC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var customerName = await conn.QuerySingleOrDefaultAsync<string>(customerSql, new { Id = customerId });
        if (customerName is null)
            return null;

        var periods = await conn.QueryAsync<CustomerBillingPeriodRow>(periodsSql, new { CustomerId = customerId });
        var gsrnRows = await conn.QueryAsync<(Guid BillingPeriodId, string Gsrn)>(gsrnSql, new { CustomerId = customerId });
        var gsrnsByPeriod = gsrnRows
            .GroupBy(r => r.BillingPeriodId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Gsrn).Distinct().OrderBy(s => s).ToList());

        var periodList = periods.Select(p => new CustomerBillingPeriod(
            p.BillingPeriodId,
            p.PeriodStart,
            p.PeriodEnd,
            p.TotalAmount,
            p.TotalVat,
            gsrnsByPeriod.GetValueOrDefault(p.BillingPeriodId, []))).ToList();

        var acontoRows = await conn.QueryAsync<AcontoPrepaymentRow>(acontoSql, new { CustomerId = customerId });
        var acontoList = acontoRows.Select(a => new AcontoPrepaymentInfo(
            a.InvoiceId,
            a.InvoiceNumber,
            a.PeriodStart,
            a.PeriodEnd,
            a.Amount,
            a.Currency)).ToList();

        var totalBilled = periodList.Sum(p => p.TotalAmount);
        var totalPaid = acontoList.Sum(a => a.Amount);

        return new CustomerBillingSummary(
            customerId,
            customerName,
            periodList,
            acontoList,
            totalBilled,
            totalPaid);
    }

}

// DTOs for Dapper mapping (DateOnly for PostgreSQL DATE columns, Npgsql 9.0+ returns DateOnly by default)
internal class BillingPeriodRow
{
    public Guid Id { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Frequency { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public int TotalCount { get; set; }
    public int SettlementRunCount { get; set; }
}

internal class SettlementRunRow
{
    public Guid Id { get; set; }
    public Guid BillingPeriodId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string GridAreaCode { get; set; } = null!;
    public int Version { get; set; }
    public string Status { get; set; } = null!;
    public DateTime ExecutedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TotalCount { get; set; }
    public string MeteringPointId { get; set; } = null!;
    public Guid? CustomerId { get; set; }
}

internal class SettlementRunDetailRow
{
    public Guid Id { get; set; }
    public Guid BillingPeriodId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string GridAreaCode { get; set; } = null!;
    public int Version { get; set; }
    public string Status { get; set; } = null!;
    public DateTime ExecutedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string MeteringPointId { get; set; } = null!;
    public Guid? CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalVat { get; set; }
    public string? ErrorDetails { get; set; }
}

internal class SettlementLineSummaryRow
{
    public Guid Id { get; set; }
    public Guid SettlementRunId { get; set; }
    public string MeteringPointGsrn { get; set; } = null!;
    public string ChargeType { get; set; } = null!;
    public decimal TotalKwh { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Currency { get; set; } = null!;
    public int TotalCount { get; set; }
}

internal class SettlementLineDetailRow
{
    public Guid Id { get; set; }
    public Guid SettlementRunId { get; set; }
    public string MeteringPointGsrn { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string ChargeType { get; set; } = null!;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal TotalKwh { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string Currency { get; set; } = null!;
}

internal class CustomerBillingPeriodRow
{
    public Guid BillingPeriodId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalVat { get; set; }
}

internal class AcontoPrepaymentRow
{
    public Guid InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
}
