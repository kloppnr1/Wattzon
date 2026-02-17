using Dapper;
using DataHub.Settlement.Application.Common;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Infrastructure.Database;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Settlement;

public sealed class CorrectionRepository : ICorrectionRepository
{
    private readonly string _connectionString;

    static CorrectionRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        DapperTypeHandlers.Register();
    }

    public CorrectionRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task StoreCorrectionAsync(Guid batchId, CorrectionResult result, Guid? originalRunId, string triggerType, string? note, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO settlement.correction_settlement
                (correction_batch_id, metering_point_id, period_start, period_end, original_run_id,
                 delta_kwh, charge_type, delta_amount, trigger_type, status, vat_amount, total_amount, note)
            VALUES
                (@BatchId, @MeteringPointId, @PeriodStart, @PeriodEnd, @OriginalRunId,
                 @DeltaKwh, @ChargeType, @DeltaAmount, @TriggerType, 'completed', @VatAmount, @TotalAmount, @Note)
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var line in result.Lines)
        {
            await conn.ExecuteAsync(sql, new
            {
                BatchId = batchId,
                result.MeteringPointId,
                PeriodStart = result.PeriodStart,
                PeriodEnd = result.PeriodEnd,
                OriginalRunId = originalRunId,
                DeltaKwh = line.Kwh ?? result.TotalDeltaKwh,
                line.ChargeType,
                DeltaAmount = line.Amount,
                TriggerType = triggerType,
                result.VatAmount,
                TotalAmount = result.Total,
                Note = note,
            }, tx);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<PagedResult<CorrectionBatchSummary>> GetCorrectionsPagedAsync(
        string? meteringPointId, string? triggerType, DateOnly? fromDate, DateOnly? toDate,
        int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        var sql = """
            WITH batches AS (
                SELECT correction_batch_id,
                       metering_point_id,
                       period_start,
                       period_end,
                       original_run_id,
                       MAX(delta_kwh) AS total_delta_kwh,
                       SUM(delta_amount) AS subtotal,
                       MAX(vat_amount) AS vat_amount,
                       MAX(total_amount) AS total,
                       MAX(trigger_type) AS trigger_type,
                       MAX(status) AS status,
                       MAX(note) AS note,
                       MAX(created_at) AS created_at,
                       COUNT(*) OVER() AS total_count
                FROM settlement.correction_settlement
                WHERE 1=1
            """;

        if (!string.IsNullOrEmpty(meteringPointId))
            sql += " AND metering_point_id = @MeteringPointId\n";
        if (!string.IsNullOrEmpty(triggerType))
            sql += " AND trigger_type = @TriggerType\n";
        if (fromDate.HasValue)
            sql += " AND created_at >= @FromDate\n";
        if (toDate.HasValue)
            sql += " AND created_at < @ToDate\n";

        sql += """
                GROUP BY correction_batch_id, metering_point_id, period_start, period_end, original_run_id
                ORDER BY created_at DESC
                LIMIT @PageSize OFFSET @Offset
            )
            SELECT * FROM batches
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<CorrectionBatchRow>(sql, new
        {
            MeteringPointId = meteringPointId,
            TriggerType = triggerType,
            FromDate = fromDate,
            ToDate = toDate?.AddDays(1),
            PageSize = pageSize,
            Offset = offset,
        });

        var rowList = rows.ToList();
        var totalCount = rowList.FirstOrDefault()?.TotalCount ?? 0;

        var items = rowList.Select(r => new CorrectionBatchSummary(
            r.CorrectionBatchId,
            r.MeteringPointId,
            r.PeriodStart,
            r.PeriodEnd,
            r.OriginalRunId,
            r.TotalDeltaKwh,
            r.Subtotal,
            r.VatAmount,
            r.Total,
            r.TriggerType,
            r.Status,
            r.Note,
            r.CreatedAt)).ToList();

        return new PagedResult<CorrectionBatchSummary>(items, totalCount, page, pageSize);
    }

    public async Task<CorrectionBatchDetail?> GetCorrectionAsync(Guid correctionBatchId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, correction_batch_id, metering_point_id, period_start, period_end,
                   original_run_id, delta_kwh, charge_type, delta_amount, trigger_type,
                   status, vat_amount, total_amount, note, created_at
            FROM settlement.correction_settlement
            WHERE correction_batch_id = @BatchId
            ORDER BY charge_type
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = (await conn.QueryAsync<CorrectionLineRow>(sql, new { BatchId = correctionBatchId })).ToList();

        if (rows.Count == 0)
            return null;

        var first = rows[0];
        var lines = rows.Select(r => new CorrectionLineDetail(r.Id, r.ChargeType, r.DeltaKwh, r.DeltaAmount)).ToList();

        return new CorrectionBatchDetail(
            first.CorrectionBatchId,
            first.MeteringPointId,
            first.PeriodStart,
            first.PeriodEnd,
            first.OriginalRunId,
            first.DeltaKwh,
            lines.Sum(l => l.DeltaAmount),
            first.VatAmount,
            first.TotalAmount,
            first.TriggerType,
            first.Status,
            first.Note,
            first.CreatedAt,
            lines);
    }

    public async Task<IReadOnlyList<CorrectionBatchSummary>> GetCorrectionsForRunAsync(Guid originalRunId, CancellationToken ct)
    {
        const string sql = """
            SELECT correction_batch_id,
                   metering_point_id,
                   period_start,
                   period_end,
                   original_run_id,
                   MAX(delta_kwh) AS total_delta_kwh,
                   SUM(delta_amount) AS subtotal,
                   MAX(vat_amount) AS vat_amount,
                   MAX(total_amount) AS total,
                   MAX(trigger_type) AS trigger_type,
                   MAX(status) AS status,
                   MAX(note) AS note,
                   MAX(created_at) AS created_at
            FROM settlement.correction_settlement
            WHERE original_run_id = @OriginalRunId
            GROUP BY correction_batch_id, metering_point_id, period_start, period_end, original_run_id
            ORDER BY created_at DESC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<CorrectionBatchRow>(sql, new { OriginalRunId = originalRunId });

        return rows.Select(r => new CorrectionBatchSummary(
            r.CorrectionBatchId,
            r.MeteringPointId,
            r.PeriodStart,
            r.PeriodEnd,
            r.OriginalRunId,
            r.TotalDeltaKwh,
            r.Subtotal,
            r.VatAmount,
            r.Total,
            r.TriggerType,
            r.Status,
            r.Note,
            r.CreatedAt)).ToList();
    }
}

internal class CorrectionBatchRow
{
    public Guid CorrectionBatchId { get; set; }
    public string MeteringPointId { get; set; } = null!;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public Guid? OriginalRunId { get; set; }
    public decimal TotalDeltaKwh { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string TriggerType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalCount { get; set; }
}

internal class CorrectionLineRow
{
    public Guid Id { get; set; }
    public Guid CorrectionBatchId { get; set; }
    public string MeteringPointId { get; set; } = null!;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public Guid? OriginalRunId { get; set; }
    public decimal DeltaKwh { get; set; }
    public string ChargeType { get; set; } = null!;
    public decimal DeltaAmount { get; set; }
    public string TriggerType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
