using Dapper;
using DataHub.Settlement.Application.Onboarding;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Onboarding;

public sealed class SignupRepository : ISignupRepository
{
    private readonly string _connectionString;

    static SignupRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Database.DapperTypeHandlers.Register();
    }

    public SignupRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<string> NextSignupNumberAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT 'SGN-' || EXTRACT(YEAR FROM now())::TEXT || '-' ||
                   LPAD(nextval('portfolio.signup_number_seq')::TEXT, 5, '0')
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<Signup> CreateAsync(string signupNumber, string darId, string gsrn, Guid customerId,
        Guid productId, Guid processRequestId, string type, DateOnly effectiveDate, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.signup
                (signup_number, dar_id, gsrn, customer_id, product_id, process_request_id, type, effective_date)
            VALUES
                (@SignupNumber, @DarId, @Gsrn, @CustomerId, @ProductId, @ProcessRequestId, @Type, @EffectiveDate)
            RETURNING id, signup_number, dar_id, gsrn, customer_id, product_id, process_request_id,
                      type, effective_date, status, rejection_reason
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<Signup>(
            new CommandDefinition(sql, new
            {
                SignupNumber = signupNumber, DarId = darId, Gsrn = gsrn, CustomerId = customerId,
                ProductId = productId, ProcessRequestId = processRequestId, Type = type, EffectiveDate = effectiveDate
            }, cancellationToken: ct));
    }

    public async Task<Signup?> GetBySignupNumberAsync(string signupNumber, CancellationToken ct)
    {
        const string sql = """
            SELECT id, signup_number, dar_id, gsrn, customer_id, product_id, process_request_id,
                   type, effective_date, status, rejection_reason
            FROM portfolio.signup
            WHERE signup_number = @SignupNumber
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Signup>(
            new CommandDefinition(sql, new { SignupNumber = signupNumber }, cancellationToken: ct));
    }

    public async Task<Signup?> GetByProcessRequestIdAsync(Guid processRequestId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, signup_number, dar_id, gsrn, customer_id, product_id, process_request_id,
                   type, effective_date, status, rejection_reason
            FROM portfolio.signup
            WHERE process_request_id = @ProcessRequestId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Signup>(
            new CommandDefinition(sql, new { ProcessRequestId = processRequestId }, cancellationToken: ct));
    }

    public async Task<Signup?> GetActiveByGsrnAsync(string gsrn, CancellationToken ct)
    {
        const string sql = """
            SELECT id, signup_number, dar_id, gsrn, customer_id, product_id, process_request_id,
                   type, effective_date, status, rejection_reason
            FROM portfolio.signup
            WHERE gsrn = @Gsrn AND status NOT IN ('active', 'rejected', 'cancelled')
            ORDER BY created_at DESC
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Signup>(
            new CommandDefinition(sql, new { Gsrn = gsrn }, cancellationToken: ct));
    }

    public async Task UpdateStatusAsync(Guid id, string status, string? rejectionReason, CancellationToken ct)
    {
        const string sql = """
            UPDATE portfolio.signup
            SET status = @Status, rejection_reason = @RejectionReason, updated_at = now()
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, Status = status, RejectionReason = rejectionReason }, cancellationToken: ct));
    }

    public async Task SetProcessRequestIdAsync(Guid id, Guid processRequestId, CancellationToken ct)
    {
        const string sql = """
            UPDATE portfolio.signup
            SET process_request_id = @ProcessRequestId, updated_at = now()
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, ProcessRequestId = processRequestId }, cancellationToken: ct));
    }

    public async Task<string?> GetCustomerCprCvrAsync(Guid signupId, CancellationToken ct)
    {
        const string sql = """
            SELECT c.cpr_cvr
            FROM portfolio.signup s
            JOIN portfolio.customer c ON c.id = s.customer_id
            WHERE s.id = @SignupId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(sql, new { SignupId = signupId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SignupListItem>> GetAllAsync(string? statusFilter, CancellationToken ct)
    {
        var sql = """
            SELECT s.id, s.signup_number, s.gsrn, s.type, s.effective_date, s.status,
                   s.rejection_reason, c.name AS customer_name, s.created_at
            FROM portfolio.signup s
            JOIN portfolio.customer c ON c.id = s.customer_id
            """;

        if (!string.IsNullOrEmpty(statusFilter))
            sql += " WHERE s.status = @Status";

        sql += " ORDER BY s.created_at DESC";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var result = await conn.QueryAsync<SignupListItem>(
            new CommandDefinition(sql, new { Status = statusFilter }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<SignupDetail?> GetDetailByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT s.id, s.signup_number, s.dar_id, s.gsrn, s.type, s.effective_date, s.status,
                   s.rejection_reason, s.customer_id, c.name AS customer_name, c.cpr_cvr,
                   c.contact_type, s.product_id, p.name AS product_name,
                   s.process_request_id, s.created_at, s.updated_at
            FROM portfolio.signup s
            JOIN portfolio.customer c ON c.id = s.customer_id
            JOIN portfolio.product p ON p.id = s.product_id
            WHERE s.id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SignupDetail>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }
}
