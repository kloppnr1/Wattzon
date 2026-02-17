using Dapper;
using DataHub.Settlement.Application.Portfolio;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Portfolio;

public sealed class PortfolioRepository : IPortfolioRepository
{
    private readonly string _connectionString;

    static PortfolioRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Database.DapperTypeHandlers.Register();
    }

    public PortfolioRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task EnsureGridAreaAsync(string code, string gridOperatorGln, string gridOperatorName, string priceArea, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.grid_area (code, grid_operator_gln, grid_operator_name, price_area)
            VALUES (@Code, @GridOperatorGln, @GridOperatorName, @PriceArea)
            ON CONFLICT (code) DO UPDATE SET
                grid_operator_gln = EXCLUDED.grid_operator_gln,
                grid_operator_name = EXCLUDED.grid_operator_name,
                price_area = EXCLUDED.price_area
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Code = code, GridOperatorGln = gridOperatorGln, GridOperatorName = gridOperatorName, PriceArea = priceArea },
            cancellationToken: ct));
    }

    public async Task<Customer> CreateCustomerAsync(string name, string cprCvr, string contactType, Address? billingAddress, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.customer (name, cpr_cvr, contact_type,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city)
            VALUES (@Name, @CprCvr, @ContactType,
                @BillingDarId, @BillingStreet, @BillingHouseNumber, @BillingFloor, @BillingDoor, @BillingPostalCode, @BillingCity)
            RETURNING id, name, cpr_cvr, contact_type, status,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleAsync<CustomerRow>(
            new CommandDefinition(sql, new
            {
                Name = name, CprCvr = cprCvr, ContactType = contactType,
                BillingDarId = billingAddress?.DarId,
                BillingStreet = billingAddress?.Street,
                BillingHouseNumber = billingAddress?.HouseNumber,
                BillingFloor = billingAddress?.Floor,
                BillingDoor = billingAddress?.Door,
                BillingPostalCode = billingAddress?.PostalCode,
                BillingCity = billingAddress?.City,
            }, cancellationToken: ct));
        return row.ToCustomer();
    }

    public async Task<Customer?> GetCustomerByCprCvrAsync(string cprCvr, CancellationToken ct)
    {
        const string sql = """
            SELECT id, name, cpr_cvr, contact_type, status,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city
            FROM portfolio.customer
            WHERE cpr_cvr = @CprCvr
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CustomerRow>(
            new CommandDefinition(sql, new { CprCvr = cprCvr }, cancellationToken: ct));
        return row?.ToCustomer();
    }

    public async Task<MeteringPoint?> GetMeteringPointByGsrnAsync(string gsrn, CancellationToken ct)
    {
        const string sql = """
            SELECT gsrn, type, settlement_method, grid_area_code, grid_operator_gln, price_area, connection_status
            FROM portfolio.metering_point
            WHERE gsrn = @Gsrn
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<MeteringPoint>(
            new CommandDefinition(sql, new { Gsrn = gsrn }, cancellationToken: ct));
    }

    public async Task<MeteringPoint> CreateMeteringPointAsync(MeteringPoint mp, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, grid_area_code, grid_operator_gln, price_area)
            VALUES (@Gsrn, @Type, @SettlementMethod, @GridAreaCode, @GridOperatorGln, @PriceArea)
            RETURNING gsrn, type, settlement_method, grid_area_code, grid_operator_gln, price_area, connection_status
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<MeteringPoint>(
            new CommandDefinition(sql, mp, cancellationToken: ct));
    }

    public async Task<Product> CreateProductAsync(string name, string energyModel, decimal marginOrePerKwh,
        decimal? supplementOrePerKwh, decimal subscriptionKrPerMonth, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.product (name, energy_model, margin_ore_per_kwh, supplement_ore_per_kwh, subscription_kr_per_month)
            VALUES (@Name, @EnergyModel, @MarginOrePerKwh, @SupplementOrePerKwh, @SubscriptionKrPerMonth)
            RETURNING id, name, energy_model, margin_ore_per_kwh, supplement_ore_per_kwh, subscription_kr_per_month,
                      description, green_energy, display_order
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<Product>(
            new CommandDefinition(sql,
                new { Name = name, EnergyModel = energyModel, MarginOrePerKwh = marginOrePerKwh, SupplementOrePerKwh = supplementOrePerKwh, SubscriptionKrPerMonth = subscriptionKrPerMonth },
                cancellationToken: ct));
    }

    public async Task<Contract> CreateContractAsync(Guid customerId, string gsrn, Guid productId,
        string billingFrequency, string paymentModel, DateOnly startDate, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.contract (customer_id, gsrn, product_id, billing_frequency, payment_model, start_date)
            VALUES (@CustomerId, @Gsrn, @ProductId, @BillingFrequency, @PaymentModel, @StartDate)
            RETURNING id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date, payer_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<Contract>(
            new CommandDefinition(sql,
                new { CustomerId = customerId, Gsrn = gsrn, ProductId = productId, BillingFrequency = billingFrequency, PaymentModel = paymentModel, StartDate = startDate },
                cancellationToken: ct));
    }

    public async Task<SupplyPeriod> CreateSupplyPeriodAsync(string gsrn, DateOnly startDate, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.supply_period (gsrn, start_date)
            VALUES (@Gsrn, @StartDate)
            RETURNING id, gsrn, start_date, end_date
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        try
        {
            return await conn.QuerySingleAsync<SupplyPeriod>(
                new CommandDefinition(sql, new { Gsrn = gsrn, StartDate = startDate }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException($"An active supply period already exists for GSRN {gsrn}.", ex);
        }
    }

    public async Task ActivateMeteringPointAsync(string gsrn, DateTime activatedAtUtc, CancellationToken ct)
    {
        const string sql = """
            UPDATE portfolio.metering_point
            SET activated_at = @ActivatedAt, updated_at = now()
            WHERE gsrn = @Gsrn
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Gsrn = gsrn, ActivatedAt = activatedAtUtc }, cancellationToken: ct));
    }

    public async Task<Contract?> GetActiveContractAsync(string gsrn, CancellationToken ct)
    {
        const string sql = """
            SELECT id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date, payer_id
            FROM portfolio.contract
            WHERE gsrn = @Gsrn AND end_date IS NULL
            ORDER BY start_date DESC
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Contract>(
            new CommandDefinition(sql, new { Gsrn = gsrn }, cancellationToken: ct));
    }

    public async Task<Contract?> GetLatestContractByGsrnAsync(string gsrn, CancellationToken ct)
    {
        const string sql = """
            SELECT id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date, payer_id
            FROM portfolio.contract
            WHERE gsrn = @Gsrn
            ORDER BY start_date DESC
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Contract>(
            new CommandDefinition(sql, new { Gsrn = gsrn }, cancellationToken: ct));
    }

    public async Task<Product?> GetProductAsync(Guid productId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, name, energy_model, margin_ore_per_kwh, supplement_ore_per_kwh, subscription_kr_per_month,
                   description, green_energy, display_order
            FROM portfolio.product
            WHERE id = @ProductId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Product>(
            new CommandDefinition(sql, new { ProductId = productId }, cancellationToken: ct));
    }

    public async Task DeactivateMeteringPointAsync(string gsrn, DateTime deactivatedAtUtc, CancellationToken ct)
    {
        const string sql = """
            UPDATE portfolio.metering_point
            SET deactivated_at = @DeactivatedAt, connection_status = 'disconnected', updated_at = now()
            WHERE gsrn = @Gsrn
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Gsrn = gsrn, DeactivatedAt = deactivatedAtUtc }, cancellationToken: ct));
    }

    public async Task EndSupplyPeriodAsync(string gsrn, DateOnly endDate, string endReason, CancellationToken ct)
    {
        const string sql = """
            UPDATE portfolio.supply_period
            SET end_date = @EndDate, end_reason = @EndReason
            WHERE gsrn = @Gsrn AND end_date IS NULL
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Gsrn = gsrn, EndDate = endDate, EndReason = endReason }, cancellationToken: ct));
    }

    public async Task EndContractAsync(string gsrn, DateOnly endDate, CancellationToken ct)
    {
        const string sql = """
            UPDATE portfolio.contract
            SET end_date = @EndDate, updated_at = now()
            WHERE gsrn = @Gsrn AND end_date IS NULL
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Gsrn = gsrn, EndDate = endDate }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SupplyPeriod>> GetSupplyPeriodsAsync(string gsrn, CancellationToken ct)
    {
        const string sql = """
            SELECT id, gsrn, start_date, end_date
            FROM portfolio.supply_period
            WHERE gsrn = @Gsrn
            ORDER BY start_date
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var result = await conn.QueryAsync<SupplyPeriod>(
            new CommandDefinition(sql, new { Gsrn = gsrn }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task UpdateMeteringPointGridAreaAsync(string gsrn, string newGridAreaCode, string newPriceArea, CancellationToken ct)
    {
        const string sql = """
            UPDATE portfolio.metering_point
            SET grid_area_code = @NewGridAreaCode, price_area = @NewPriceArea, updated_at = now()
            WHERE gsrn = @Gsrn
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Gsrn = gsrn, NewGridAreaCode = newGridAreaCode, NewPriceArea = newPriceArea }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT id, name, energy_model, margin_ore_per_kwh, supplement_ore_per_kwh,
                   subscription_kr_per_month, description, green_energy, display_order
            FROM portfolio.product
            WHERE is_active = true
            ORDER BY display_order, name
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var result = await conn.QueryAsync<Product>(
            new CommandDefinition(sql, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<Customer?> GetCustomerAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id, name, cpr_cvr, contact_type, status,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city
            FROM portfolio.customer
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CustomerRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
        return row?.ToCustomer();
    }

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT id, name, cpr_cvr, contact_type, status,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city
            FROM portfolio.customer
            ORDER BY name
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var result = await conn.QueryAsync<CustomerRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return result.Select(r => r.ToCustomer()).ToList();
    }

    public async Task<PagedResult<Customer>> GetCustomersPagedAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var whereClause = hasSearch ? "WHERE name ILIKE @Search OR cpr_cvr ILIKE @Search" : "";

        var countSql = $"SELECT COUNT(*) FROM portfolio.customer {whereClause}";
        var dataSql = $"""
            SELECT id, name, cpr_cvr, contact_type, status,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city
            FROM portfolio.customer
            {whereClause}
            ORDER BY name
            LIMIT @PageSize OFFSET @Offset
            """;

        var parameters = new
        {
            Search = hasSearch ? $"%{search}%" : null,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        };

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var totalCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: ct));
        var items = await conn.QueryAsync<CustomerRow>(
            new CommandDefinition(dataSql, parameters, cancellationToken: ct));

        return new PagedResult<Customer>(items.Select(r => r.ToCustomer()).ToList(), totalCount, page, pageSize);
    }

    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*)::int FROM portfolio.signup WHERE status IN ('registered', 'processing')) AS "PendingSignups",
                (SELECT COUNT(*)::int FROM portfolio.customer WHERE status = 'active') AS "ActiveCustomers",
                (SELECT COUNT(*)::int FROM portfolio.signup WHERE status = 'rejected') AS "RejectedSignups",
                (SELECT COUNT(*)::int FROM portfolio.product WHERE is_active = true) AS "ProductCount"
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleAsync<DashboardStats>(
            new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Contract>> GetContractsForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date, payer_id
            FROM portfolio.contract
            WHERE customer_id = @CustomerId
            ORDER BY start_date DESC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var result = await conn.QueryAsync<Contract>(
            new CommandDefinition(sql, new { CustomerId = customerId }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<IReadOnlyList<MeteringPointWithSupply>> GetMeteringPointsForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        const string sql = """
            SELECT DISTINCT mp.gsrn, mp.type, mp.settlement_method, mp.grid_area_code,
                   mp.price_area, mp.connection_status,
                   sp.start_date AS supply_start, sp.end_date AS supply_end
            FROM portfolio.contract c
            JOIN portfolio.metering_point mp ON mp.gsrn = c.gsrn
            LEFT JOIN portfolio.supply_period sp ON sp.gsrn = c.gsrn AND sp.end_date IS NULL
            WHERE c.customer_id = @CustomerId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var result = await conn.QueryAsync<MeteringPointWithSupply>(
            new CommandDefinition(sql, new { CustomerId = customerId }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<Payer> CreatePayerAsync(string name, string cprCvr, string contactType,
        string? email, string? phone, Address? billingAddress, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.payer (name, cpr_cvr, contact_type, email, phone,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city)
            VALUES (@Name, @CprCvr, @ContactType, @Email, @Phone,
                @BillingDarId, @BillingStreet, @BillingHouseNumber, @BillingFloor, @BillingDoor, @BillingPostalCode, @BillingCity)
            RETURNING id, name, cpr_cvr, contact_type, email, phone,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleAsync<PayerRow>(
            new CommandDefinition(sql, new
            {
                Name = name, CprCvr = cprCvr, ContactType = contactType, Email = email, Phone = phone,
                BillingDarId = billingAddress?.DarId,
                BillingStreet = billingAddress?.Street,
                BillingHouseNumber = billingAddress?.HouseNumber,
                BillingFloor = billingAddress?.Floor,
                BillingDoor = billingAddress?.Door,
                BillingPostalCode = billingAddress?.PostalCode,
                BillingCity = billingAddress?.City,
            }, cancellationToken: ct));
        return row.ToPayer();
    }

    public async Task<Payer?> GetPayerAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id, name, cpr_cvr, contact_type, email, phone,
                billing_dar_id, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city
            FROM portfolio.payer
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<PayerRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
        return row?.ToPayer();
    }

    public async Task<IReadOnlyList<Payer>> GetPayersForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        const string sql = """
            SELECT DISTINCT p.id, p.name, p.cpr_cvr, p.contact_type, p.email, p.phone,
                p.billing_dar_id, p.billing_street, p.billing_house_number, p.billing_floor, p.billing_door, p.billing_postal_code, p.billing_city
            FROM portfolio.payer p
            JOIN portfolio.contract c ON c.payer_id = p.id
            WHERE c.customer_id = @CustomerId
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var result = await conn.QueryAsync<PayerRow>(
            new CommandDefinition(sql, new { CustomerId = customerId }, cancellationToken: ct));
        return result.Select(r => r.ToPayer()).ToList();
    }

    public async Task UpdateCustomerBillingAddressAsync(Guid customerId, Address address, CancellationToken ct)
    {
        const string sql = """
            UPDATE portfolio.customer
            SET billing_dar_id = @DarId,
                billing_street = @Street, billing_house_number = @HouseNumber,
                billing_floor = @Floor, billing_door = @Door,
                billing_postal_code = @PostalCode, billing_city = @City,
                updated_at = now()
            WHERE id = @Id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = customerId,
            address.DarId, address.Street, address.HouseNumber, address.Floor,
            address.Door, address.PostalCode, address.City,
        }, cancellationToken: ct));
    }

    public async Task StageCustomerDataAsync(string gsrn, string customerName, string? cprCvr, string customerType, string? phone, string? email, string? correlationId, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO portfolio.customer_data_staging (gsrn, customer_name, cpr_cvr, customer_type, phone, email, correlation_id)
            VALUES (@Gsrn, @CustomerName, @CprCvr, @CustomerType, @Phone, @Email, @CorrelationId)
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Gsrn = gsrn, CustomerName = customerName, CprCvr = cprCvr, CustomerType = customerType, Phone = phone, Email = email, CorrelationId = correlationId },
            cancellationToken: ct));
    }

    public async Task<StagedCustomerData?> GetStagedCustomerDataAsync(string gsrn, CancellationToken ct)
    {
        const string sql = """
            SELECT gsrn, customer_name, cpr_cvr, customer_type, phone, email, correlation_id
            FROM portfolio.customer_data_staging
            WHERE gsrn = @Gsrn
            ORDER BY created_at DESC
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<StagedCustomerData>(
            new CommandDefinition(sql, new { Gsrn = gsrn }, cancellationToken: ct));
    }

    // --- Internal row types for Dapper mapping ---

    private record CustomerRow(
        Guid Id, string Name, string CprCvr, string ContactType, string Status,
        string? BillingDarId, string? BillingStreet, string? BillingHouseNumber, string? BillingFloor,
        string? BillingDoor, string? BillingPostalCode, string? BillingCity)
    {
        public Customer ToCustomer()
        {
            var hasAddress = BillingStreet is not null || BillingPostalCode is not null || BillingCity is not null;
            var address = hasAddress
                ? new Address(BillingStreet, BillingHouseNumber, BillingFloor, BillingDoor, BillingPostalCode, BillingCity, BillingDarId)
                : null;
            return new Customer(Id, Name, CprCvr, ContactType, Status, address);
        }
    }

    private record PayerRow(
        Guid Id, string Name, string CprCvr, string ContactType, string? Email, string? Phone,
        string? BillingDarId, string? BillingStreet, string? BillingHouseNumber, string? BillingFloor,
        string? BillingDoor, string? BillingPostalCode, string? BillingCity)
    {
        public Payer ToPayer()
        {
            var hasAddress = BillingStreet is not null || BillingPostalCode is not null || BillingCity is not null;
            var address = hasAddress
                ? new Address(BillingStreet, BillingHouseNumber, BillingFloor, BillingDoor, BillingPostalCode, BillingCity, BillingDarId)
                : null;
            return new Payer(Id, Name, CprCvr, ContactType, Email, Phone, address);
        }
    }
}
