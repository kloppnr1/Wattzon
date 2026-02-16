using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Infrastructure;
using DataHub.Settlement.Infrastructure.AddressLookup;
using DataHub.Settlement.Infrastructure.Billing;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.Infrastructure.Settlement;
using DataHub.Settlement.Infrastructure.Tariff;
using DataHub.Settlement.Application.Authentication;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Infrastructure.Authentication;
using DataHub.Settlement.Infrastructure.Parsing;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SettlementDb")
    ?? Environment.GetEnvironmentVariable("SETTLEMENT_DB_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Database connection string not configured. Set ConnectionStrings__SettlementDb or SETTLEMENT_DB_CONNECTION_STRING environment variable.");

// CORS for back-office dev (React on :5173)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Core services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IAddressLookupClient, StubAddressLookupClient>();
builder.Services.AddSingleton<IPortfolioRepository>(new PortfolioRepository(connectionString));
builder.Services.AddSingleton<IProcessRepository>(new ProcessRepository(connectionString));
builder.Services.AddSingleton<ISignupRepository>(new SignupRepository(connectionString));
// DataHub client: requires DataHub__BaseUrl to be configured
var dataHubBaseUrl = builder.Configuration["DataHub:BaseUrl"]
    ?? throw new InvalidOperationException("DataHub:BaseUrl is not configured. Set the DataHub__BaseUrl environment variable.");

builder.Services.AddHttpClient<HttpDataHubClient>(client =>
{
    client.BaseAddress = new Uri(dataHubBaseUrl);
});

builder.Services.AddSingleton<IDataHubClient>(sp =>
{
    var innerHttpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    innerHttpClient.BaseAddress = new Uri(dataHubBaseUrl);
    var inner = new HttpDataHubClient(innerHttpClient);
    var tokenProvider = sp.GetRequiredService<IAuthTokenProvider>();
    var logger = sp.GetRequiredService<ILogger<ResilientDataHubClient>>();
    return new ResilientDataHubClient(inner, tokenProvider, logger);
});

builder.Services.AddSingleton<IAuthTokenProvider>(sp =>
{
    var options = new AuthTokenOptions(
        builder.Configuration["DataHub:TenantId"] ?? "",
        builder.Configuration["DataHub:ClientId"] ?? "",
        builder.Configuration["DataHub:ClientSecret"] ?? "",
        builder.Configuration["DataHub:Scope"] ?? "");
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    return new OAuth2TokenProvider(httpClient, options);
});
builder.Services.AddSingleton<IBrsRequestBuilder, BrsRequestBuilder>();
builder.Services.AddSingleton<IOnboardingService, OnboardingService>();
builder.Services.AddSingleton<IBillingRepository>(new BillingRepository(connectionString));
builder.Services.AddSingleton<ISpotPriceRepository>(new SpotPriceRepository(connectionString));
builder.Services.AddSingleton<IMessageRepository>(new MessageRepository(connectionString));
builder.Services.AddSingleton<CorrectionEngine>();
builder.Services.AddSingleton<ICorrectionRepository>(new CorrectionRepository(connectionString));
builder.Services.AddSingleton<IMeteringDataRepository>(new MeteringDataRepository(connectionString));
builder.Services.AddSingleton<ITariffRepository>(new TariffRepository(connectionString));
builder.Services.AddSingleton<ICorrectionService, CorrectionService>();
builder.Services.AddSingleton<IInvoiceRepository>(new InvoiceRepository(connectionString));
builder.Services.AddSingleton<IPaymentRepository>(new PaymentRepository(connectionString));
builder.Services.AddSingleton<IInvoiceService, InvoiceService>();
builder.Services.AddSingleton<IPaymentAllocator>(sp =>
    new PaymentAllocator(connectionString, sp.GetRequiredService<ILogger<PaymentAllocator>>()));
builder.Services.AddSingleton<IPaymentMatchingService, PaymentMatchingService>();

// Settlement engine & data loader (used by settlement preview endpoint)
builder.Services.AddSingleton<ISettlementEngine>(new SettlementEngine());
builder.Services.AddSingleton<ISettlementDataLoader>(new SettlementDataLoader(
    new MeteringDataRepository(connectionString),
    new SpotPriceRepository(connectionString),
    new TariffRepository(connectionString)));
builder.Services.AddSingleton<IMeteringCompletenessChecker>(new MeteringCompletenessChecker(connectionString));

// Services needed for dead-letter retry (reprocessing messages through QueuePollerService)
builder.Services.AddSingleton<ICimParser, CimJsonParser>();
builder.Services.AddSingleton<IMessageLog>(new MessageLog(connectionString));
builder.Services.AddSingleton<SettlementMetrics>();
builder.Services.AddSingleton<EffectuationService>(sp =>
    new EffectuationService(
        connectionString,
        sp.GetRequiredService<IOnboardingService>(),
        sp.GetRequiredService<IInvoiceService>(),
        sp.GetRequiredService<IDataHubClient>(),
        sp.GetRequiredService<IBrsRequestBuilder>(),
        sp.GetRequiredService<IMessageRepository>(),
        sp.GetRequiredService<IClock>(),
        sp.GetRequiredService<ILogger<EffectuationService>>()));
builder.Services.AddSingleton<MasterDataMessageHandler>();
builder.Services.AddSingleton<QueuePollerService>();

var app = builder.Build();

// Run database migrations
var migrationLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrator");
DatabaseMigrator.Migrate(connectionString, migrationLogger);

app.UseCors();
app.UseStaticFiles();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Seed test data
app.MapGet("/api/seed", async () =>
{
    try
    {
        await DatabaseSeeder.SeedAsync(connectionString);
        return Results.Ok(new { message = "Database seeded successfully." });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.ToString(), statusCode: 500);
    }
});

// --- Products ---

// GET /api/products — list active products
app.MapGet("/api/products", async (IPortfolioRepository repo, CancellationToken ct) =>
{
    var products = await repo.GetActiveProductsAsync(ct);
    return Results.Ok(products.Select(p => new
    {
        p.Id,
        p.Name,
        p.EnergyModel,
        margin_ore_per_kwh = p.MarginOrePerKwh,
        supplement_ore_per_kwh = p.SupplementOrePerKwh,
        subscription_kr_per_month = p.SubscriptionKrPerMonth,
        p.Description,
        green_energy = p.GreenEnergy,
    }));
});

// --- Signups (sales channel) ---

// POST /api/signup — create a new signup
app.MapPost("/api/signup", async (SignupRequest request, IOnboardingService service, CancellationToken ct) =>
{
    try
    {
        var response = await service.CreateSignupAsync(request, ct);
        return Results.Created($"/api/signup/{response.SignupId}/status", response);
    }
    catch (ValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (ConflictException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

// GET /api/signup/{id}/status — check signup progress
app.MapGet("/api/signup/{id}/status", async (string id, IOnboardingService service, CancellationToken ct) =>
{
    var status = await service.GetStatusAsync(id, ct);
    return status is not null ? Results.Ok(status) : Results.NotFound();
});

// POST /api/signup/{id}/cancel — cancel before activation
app.MapPost("/api/signup/{id}/cancel", async (string id, IOnboardingService service, CancellationToken ct) =>
{
    try
    {
        await service.CancelAsync(id, ct);
        return Results.Ok(new { message = "Signup cancelled." });
    }
    catch (ValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (ConflictException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

// --- Dashboard ---

// GET /api/dashboard/stats — aggregated counts for dashboard
app.MapGet("/api/dashboard/stats", async (IPortfolioRepository repo, CancellationToken ct) =>
{
    var stats = await repo.GetDashboardStatsAsync(ct);
    return Results.Ok(stats);
});

// GET /api/dashboard/recent-signups — latest N signups
app.MapGet("/api/dashboard/recent-signups", async (int? limit, ISignupRepository repo, CancellationToken ct) =>
{
    var recent = await repo.GetRecentAsync(Math.Min(limit ?? 5, 20), ct);
    return Results.Ok(recent);
});

// --- Signups (back-office) ---

// GET /api/signups — paginated signups, optional status filter
app.MapGet("/api/signups", async (string? status, int? page, int? pageSize, ISignupRepository repo, CancellationToken ct) =>
{
    if (page.HasValue || pageSize.HasValue)
    {
        var p = Math.Max(page ?? 1, 1);
        var ps = Math.Clamp(pageSize ?? 50, 1, 200);
        var result = await repo.GetAllPagedAsync(status, p, ps, ct);
        return Results.Ok(result);
    }
    // Backward-compatible: no pagination params returns flat list (capped at 200)
    var signups = await repo.GetAllPagedAsync(status, 1, 200, ct);
    return Results.Ok(signups);
});

// GET /api/signups/{id} — full signup detail with correction chain
app.MapGet("/api/signups/{id:guid}", async (Guid id, ISignupRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetDetailByIdAsync(id, ct);
    if (detail is null) return Results.NotFound();

    var correctionChain = await repo.GetCorrectionChainAsync(id, ct);
    // Only include chain if there are linked corrections (more than just this signup)
    var chain = correctionChain.Count > 1 ? correctionChain : null;

    return Results.Ok(new
    {
        detail.Id,
        detail.SignupNumber,
        detail.DarId,
        detail.Gsrn,
        detail.Type,
        detail.EffectiveDate,
        detail.Status,
        detail.RejectionReason,
        detail.CustomerId,
        detail.CustomerName,
        detail.CprCvr,
        detail.ContactType,
        detail.ProductId,
        detail.ProductName,
        detail.ProcessRequestId,
        detail.BillingFrequency,
        detail.PaymentModel,
        detail.CreatedAt,
        detail.UpdatedAt,
        detail.CorrectedFromId,
        detail.CorrectedFromSignupNumber,
        CorrectionChain = chain,
    });
});

// GET /api/signups/{id}/events — process event timeline
app.MapGet("/api/signups/{id:guid}/events", async (Guid id, ISignupRepository signupRepo, IProcessRepository processRepo, CancellationToken ct) =>
{
    var detail = await signupRepo.GetDetailByIdAsync(id, ct);
    if (detail is null) return Results.NotFound();
    if (!detail.ProcessRequestId.HasValue) return Results.Ok(Array.Empty<object>());

    var events = await processRepo.GetEventsAsync(detail.ProcessRequestId.Value, ct);
    return Results.Ok(events.Select(e => new
    {
        e.OccurredAt,
        e.EventType,
        e.Payload,
        e.Source,
    }));
});

// --- Address lookup ---

// GET /api/address/gsrn/{gsrn} — validate GSRN directly (no address lookup)
app.MapGet("/api/address/gsrn/{gsrn}", async (string gsrn, IOnboardingService service, CancellationToken ct) =>
{
    try
    {
        var result = await service.ValidateGsrnAsync(gsrn, ct);
        return Results.Ok(result.MeteringPoints.Select(mp => new
        {
            mp.Gsrn,
            mp.Type,
            grid_area_code = mp.GridAreaCode,
            has_active_process = mp.HasActiveProcess,
        }));
    }
    catch (ValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// GET /api/address/{darId} — resolve DAR ID to GSRN(s) with active process check
app.MapGet("/api/address/{darId}", async (string darId, IOnboardingService service, CancellationToken ct) =>
{
    var result = await service.LookupAddressAsync(darId, ct);
    return Results.Ok(result.MeteringPoints.Select(mp => new
    {
        mp.Gsrn,
        mp.Type,
        grid_area_code = mp.GridAreaCode,
        has_active_process = mp.HasActiveProcess,
    }));
});

// --- Customers ---

// GET /api/customers — paginated customers with optional search
app.MapGet("/api/customers", async (int? page, int? pageSize, string? search, IPortfolioRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetCustomersPagedAsync(p, ps, search, ct);
    return Results.Ok(result);
});

// GET /api/customers/{id} — customer detail with contracts, metering points, and billing address
app.MapGet("/api/customers/{id:guid}", async (Guid id, IPortfolioRepository repo, CancellationToken ct) =>
{
    var customer = await repo.GetCustomerAsync(id, ct);
    if (customer is null) return Results.NotFound();

    var contracts = await repo.GetContractsForCustomerAsync(id, ct);
    var meteringPoints = await repo.GetMeteringPointsForCustomerAsync(id, ct);
    var payers = await repo.GetPayersForCustomerAsync(id, ct);

    var products = await repo.GetActiveProductsAsync(ct);

    return Results.Ok(new
    {
        customer.Id,
        customer.Name,
        customer.CprCvr,
        customer.ContactType,
        customer.Status,
        BillingAddress = customer.BillingAddress,
        Contracts = contracts,
        MeteringPoints = meteringPoints,
        Payers = payers,
        Products = products.Select(p => new { p.Id, p.Name }),
    });
});

// GET /api/customers/{id}/processes — DataHub processes for this customer
app.MapGet("/api/customers/{id:guid}/processes", async (Guid id, IProcessRepository repo, CancellationToken ct) =>
{
    var processes = await repo.GetByCustomerIdAsync(id, ct);
    return Results.Ok(processes.Select(p => new
    {
        p.Id,
        p.ProcessType,
        p.Gsrn,
        p.Status,
        p.EffectiveDate,
        p.DatahubCorrelationId,
    }));
});

// PUT /api/customers/{id}/billing-address — update customer billing address
app.MapPut("/api/customers/{id:guid}/billing-address", async (Guid id, Address address, IPortfolioRepository repo, CancellationToken ct) =>
{
    var customer = await repo.GetCustomerAsync(id, ct);
    if (customer is null) return Results.NotFound();

    await repo.UpdateCustomerBillingAddressAsync(id, address, ct);
    return Results.Ok(new { message = "Billing address updated." });
});

// --- Payers ---

// POST /api/payers — create a new payer
app.MapPost("/api/payers", async (Payer request, IPortfolioRepository repo, CancellationToken ct) =>
{
    var payer = await repo.CreatePayerAsync(
        request.Name, request.CprCvr, request.ContactType,
        request.Email, request.Phone, request.BillingAddress, ct);
    return Results.Created($"/api/payers/{payer.Id}", payer);
});

// GET /api/payers/{id} — payer detail
app.MapGet("/api/payers/{id:guid}", async (Guid id, IPortfolioRepository repo, CancellationToken ct) =>
{
    var payer = await repo.GetPayerAsync(id, ct);
    return payer is not null ? Results.Ok(payer) : Results.NotFound();
});

// --- Billing (back-office) ---

// GET /api/billing/periods — paginated billing periods
app.MapGet("/api/billing/periods", async (int? page, int? pageSize, IBillingRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetBillingPeriodsAsync(p, ps, ct);
    return Results.Ok(result);
});

// GET /api/billing/periods/{id} — billing period detail with settlement runs
app.MapGet("/api/billing/periods/{id:guid}", async (Guid id, IBillingRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetBillingPeriodAsync(id, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// GET /api/billing/runs — paginated settlement runs with optional filters
app.MapGet("/api/billing/runs", async (Guid? billingPeriodId, string? status, string? meteringPointId, string? gridAreaCode, DateOnly? fromDate, DateOnly? toDate, int? page, int? pageSize, IBillingRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetSettlementRunsAsync(billingPeriodId, status, meteringPointId, gridAreaCode, fromDate, toDate, p, ps, ct);
    return Results.Ok(result);
});

// GET /api/billing/runs/{id} — settlement run detail with aggregates
app.MapGet("/api/billing/runs/{id:guid}", async (Guid id, IBillingRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetSettlementRunAsync(id, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// GET /api/billing/runs/{id}/lines — paginated settlement lines for a run
app.MapGet("/api/billing/runs/{id:guid}/lines", async (Guid id, int? page, int? pageSize, IBillingRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetSettlementLinesAsync(id, p, ps, ct);
    return Results.Ok(result);
});

// GET /api/billing/metering-points/{gsrn}/lines — settlement lines by GSRN with optional date filter
app.MapGet("/api/billing/metering-points/{gsrn}/lines", async (string gsrn, DateOnly? fromDate, DateOnly? toDate, IBillingRepository repo, CancellationToken ct) =>
{
    var lines = await repo.GetSettlementLinesByMeteringPointAsync(gsrn, fromDate, toDate, ct);
    return Results.Ok(lines);
});

// GET /api/metering-points/{gsrn}/tariffs — all price elements for a metering point
app.MapGet("/api/metering-points/{gsrn}/tariffs", async (string gsrn, ITariffRepository tariffRepo, IPortfolioRepository portfolioRepo, CancellationToken ct) =>
{
    var attachments = await tariffRepo.GetAttachmentsForGsrnAsync(gsrn, ct);
    var mp = await portfolioRepo.GetMeteringPointByGsrnAsync(gsrn, ct);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    var result = new List<object>();

    // RSM-031 tariff attachments with hourly rates
    foreach (var att in attachments)
    {
        IReadOnlyList<TariffRateRow>? rates = null;
        if (mp != null && att.TariffType is "grid" or "system" or "transmission")
            rates = await tariffRepo.GetRatesAsync(mp.GridAreaCode, att.TariffType, att.ValidFrom, ct);

        result.Add(new
        {
            att.Id, att.Gsrn, att.TariffId, att.TariffType,
            att.ValidFrom, att.ValidTo, att.CorrelationId, att.CreatedAt,
            Rates = rates?.Select(r => new { r.HourNumber, r.PricePerKwh }) ?? Enumerable.Empty<object>(),
            AmountPerMonth = (decimal?)null,
            RatePerKwh = (decimal?)null,
        });
    }

    // Subscriptions and electricity tax (looked up by grid area)
    if (mp != null)
    {
        var gridSub = await tariffRepo.GetSubscriptionInfoAsync(mp.GridAreaCode, "grid", today, ct);
        if (gridSub is not null)
            result.Add(new
            {
                Id = Guid.Empty, Gsrn = gsrn, TariffId = (string?)null, TariffType = "grid_subscription",
                ValidFrom = gridSub.ValidFrom, ValidTo = (DateOnly?)null, CorrelationId = (string?)null, CreatedAt = (DateTime?)null,
                Rates = Enumerable.Empty<object>(),
                AmountPerMonth = (decimal?)gridSub.AmountPerMonth,
                RatePerKwh = (decimal?)null,
            });

        var supplierSub = await tariffRepo.GetSubscriptionInfoAsync(mp.GridAreaCode, "supplier", today, ct);
        if (supplierSub is not null)
            result.Add(new
            {
                Id = Guid.Empty, Gsrn = gsrn, TariffId = (string?)null, TariffType = "supplier_subscription",
                ValidFrom = supplierSub.ValidFrom, ValidTo = (DateOnly?)null, CorrelationId = (string?)null, CreatedAt = (DateTime?)null,
                Rates = Enumerable.Empty<object>(),
                AmountPerMonth = (decimal?)supplierSub.AmountPerMonth,
                RatePerKwh = (decimal?)null,
            });

        var elTax = await tariffRepo.GetElectricityTaxInfoAsync(today, ct);
        if (elTax is not null)
            result.Add(new
            {
                Id = Guid.Empty, Gsrn = gsrn, TariffId = (string?)null, TariffType = "electricity_tax",
                ValidFrom = elTax.ValidFrom, ValidTo = (DateOnly?)null, CorrelationId = (string?)null, CreatedAt = (DateTime?)null,
                Rates = Enumerable.Empty<object>(),
                AmountPerMonth = (decimal?)null,
                RatePerKwh = (decimal?)elTax.RatePerKwh,
            });
    }

    return Results.Ok(result);
});

// POST /api/metering-points/{gsrn}/settlement-preview — dry-run settlement (no persistence)
app.MapPost("/api/metering-points/{gsrn}/settlement-preview", async (
    string gsrn,
    SettlementPreviewRequest request,
    IPortfolioRepository portfolioRepo,
    ISettlementDataLoader dataLoader,
    ISettlementEngine engine,
    IMeteringCompletenessChecker completenessChecker,
    CancellationToken ct) =>
{
    // Look up metering point for grid area / price area
    var mp = await portfolioRepo.GetMeteringPointByGsrnAsync(gsrn, ct);
    if (mp is null)
        return Results.NotFound(new { error = "Metering point not found." });

    // Look up active contract → product for margin/supplement/subscription
    var contract = await portfolioRepo.GetActiveContractAsync(gsrn, ct);
    if (contract is null)
        return Results.BadRequest(new { error = "No active contract for this metering point." });

    var product = await portfolioRepo.GetProductAsync(contract.ProductId, ct);
    if (product is null)
        return Results.BadRequest(new { error = "Product not found for contract." });

    // Check metering data completeness
    var periodStart = request.PeriodStart;
    var periodEnd = request.PeriodEnd;
    var start = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    var end = periodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    var completeness = await completenessChecker.CheckAsync(gsrn, start, end, ct);

    // Load all settlement data
    var marginDkk = product.MarginOrePerKwh / 100m;
    var supplementDkk = (product.SupplementOrePerKwh ?? 0m) / 100m;

    SettlementInput input;
    try
    {
        input = await dataLoader.LoadAsync(
            gsrn, mp.GridAreaCode, mp.PriceArea,
            periodStart, periodEnd,
            marginDkk, supplementDkk, product.SubscriptionKrPerMonth, ct);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Could not load settlement data: {ex.Message}" });
    }

    // Run calculation
    SettlementResult result;
    try
    {
        var settlementRequest = new SettlementRequest(
            input.MeteringPointId, input.PeriodStart, input.PeriodEnd,
            input.Consumption, input.SpotPrices, input.GridTariffRates,
            input.SystemTariffRate, input.TransmissionTariffRate,
            input.ElectricityTaxRate, input.GridSubscriptionPerMonth,
            input.MarginPerKwh, input.SupplementPerKwh, input.SupplierSubscriptionPerMonth,
            input.Elvarme);

        result = engine.Calculate(settlementRequest);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    return Results.Ok(new
    {
        meteringPointId = result.MeteringPointId,
        periodStart = result.PeriodStart,
        periodEnd = result.PeriodEnd,
        totalKwh = result.TotalKwh,
        lines = result.Lines.Select(l => new { l.ChargeType, l.Kwh, l.Amount }),
        subtotal = result.Subtotal,
        vatAmount = result.VatAmount,
        total = result.Total,
        completeness = new
        {
            expectedHours = completeness.ExpectedHours,
            receivedHours = completeness.ReceivedHours,
            isComplete = completeness.IsComplete,
        },
        product = new { product.Name, product.MarginOrePerKwh, product.SupplementOrePerKwh, product.SubscriptionKrPerMonth },
        gridAreaCode = mp.GridAreaCode,
        priceArea = mp.PriceArea,
    });
});

// GET /api/billing/customers/{id}/summary — customer billing summary
app.MapGet("/api/billing/customers/{id:guid}/summary", async (Guid id, IBillingRepository repo, CancellationToken ct) =>
{
    var summary = await repo.GetCustomerBillingAsync(id, ct);
    return summary is not null ? Results.Ok(summary) : Results.NotFound();
});

// --- Corrections (back-office) ---

// GET /api/billing/corrections — paginated corrections with optional filters
app.MapGet("/api/billing/corrections", async (
    string? meteringPointId, string? triggerType, DateOnly? fromDate, DateOnly? toDate,
    int? page, int? pageSize,
    ICorrectionRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetCorrectionsPagedAsync(meteringPointId, triggerType, fromDate, toDate, p, ps, ct);
    return Results.Ok(result);
});

// GET /api/billing/corrections/{batchId} — correction detail with lines
app.MapGet("/api/billing/corrections/{batchId:guid}", async (Guid batchId, ICorrectionRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetCorrectionAsync(batchId, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// POST /api/billing/corrections — trigger manual correction
app.MapPost("/api/billing/corrections", async (TriggerCorrectionRequest request, ICorrectionService service, CancellationToken ct) =>
{
    try
    {
        var detail = await service.TriggerCorrectionAsync(request, ct);
        return Results.Created($"/api/billing/corrections/{detail.CorrectionBatchId}", detail);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// GET /api/billing/runs/{id}/corrections — corrections linked to a settlement run
app.MapGet("/api/billing/runs/{id:guid}/corrections", async (Guid id, ICorrectionRepository repo, CancellationToken ct) =>
{
    var corrections = await repo.GetCorrectionsForRunAsync(id, ct);
    return Results.Ok(corrections);
});

// --- Messages (back-office) ---

// GET /api/messages/inbound — paginated inbound messages with filters
app.MapGet("/api/messages/inbound", async (
    string? messageType, string? status, string? correlationId, string? queueName,
    DateTime? fromDate, DateTime? toDate,
    int? page, int? pageSize,
    IMessageRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var filter = new MessageFilter(messageType, status, correlationId, fromDate, toDate, queueName);
    var result = await repo.GetInboundMessagesAsync(filter, p, ps, ct);
    return Results.Ok(result);
});

// GET /api/messages/inbound/{id} — inbound message detail
app.MapGet("/api/messages/inbound/{id:guid}", async (Guid id, IMessageRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetInboundMessageAsync(id, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// GET /api/messages/outbound — paginated outbound requests with filters
app.MapGet("/api/messages/outbound", async (
    string? processType, string? status, string? correlationId,
    DateTime? fromDate, DateTime? toDate,
    int? page, int? pageSize,
    IMessageRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var filter = new OutboundFilter(processType, status, correlationId, fromDate, toDate);
    var result = await repo.GetOutboundRequestsAsync(filter, p, ps, ct);
    return Results.Ok(result);
});

// GET /api/messages/outbound/{id} — outbound request detail
app.MapGet("/api/messages/outbound/{id:guid}", async (Guid id, IMessageRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetOutboundRequestAsync(id, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// GET /api/messages/dead-letters — paginated dead letters
app.MapGet("/api/messages/dead-letters", async (bool? resolved, int? page, int? pageSize, IMessageRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetDeadLettersAsync(resolved, p, ps, ct);
    return Results.Ok(result);
});

// GET /api/messages/dead-letters/{id} — dead letter detail
app.MapGet("/api/messages/dead-letters/{id:guid}", async (Guid id, IMessageRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetDeadLetterAsync(id, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// POST /api/messages/dead-letters/{id}/retry — reprocess a dead-lettered message
app.MapPost("/api/messages/dead-letters/{id:guid}/retry", async (
    Guid id,
    IMessageRepository messageRepo,
    IMessageLog messageLog,
    QueuePollerService poller,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var deadLetter = await messageRepo.GetDeadLetterAsync(id, ct);
    if (deadLetter is null)
        return Results.NotFound(new { error = "Dead letter not found." });
    if (deadLetter.Resolved)
        return Results.BadRequest(new { error = "Dead letter is already resolved." });

    // Extract raw payload from JSONB wrapper {"raw": "..."}
    string rawPayload;
    try
    {
        var jsonDoc = System.Text.Json.JsonDocument.Parse(deadLetter.RawPayload);
        rawPayload = jsonDoc.RootElement.GetProperty("raw").GetString()!;
    }
    catch
    {
        return Results.BadRequest(new { error = "Could not extract raw payload from dead letter." });
    }

    // Look up original inbound message for message_type, correlation_id, queue_name
    if (deadLetter.OriginalMessageId is null)
        return Results.BadRequest(new { error = "Dead letter has no original message ID." });

    // Query inbound_message by datahub_message_id
    using var conn = new Npgsql.NpgsqlConnection(
        app.Configuration.GetConnectionString("SettlementDb")
        ?? Environment.GetEnvironmentVariable("SETTLEMENT_DB_CONNECTION_STRING")!);
    await conn.OpenAsync(ct);
    var inbound = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<InboundMessageInfoRow>(conn,
        "SELECT datahub_message_id, message_type, correlation_id, queue_name FROM datahub.inbound_message WHERE datahub_message_id = @MsgId",
        new { MsgId = deadLetter.OriginalMessageId });

    if (inbound is null)
        return Results.BadRequest(new { error = "Original inbound message not found." });

    // Parse queue name
    if (!Enum.TryParse<QueueName>(inbound.QueueName, ignoreCase: true, out var queue))
        return Results.BadRequest(new { error = $"Unknown queue name: {inbound.QueueName}" });

    // Clear idempotency claim so the message can be reprocessed
    await messageLog.ClearClaimAsync(inbound.DatahubMessageId, ct);

    // Reconstruct DataHubMessage and reprocess
    var message = new DataHubMessage(inbound.DatahubMessageId, inbound.MessageType, inbound.CorrelationId, rawPayload);

    try
    {
        await poller.ReprocessMessageAsync(message, queue, ct);

        // Success: resolve the dead letter and update inbound status
        await messageRepo.ResolveDeadLetterAsync(id, "retry", ct);
        await messageLog.MarkInboundStatusAsync(inbound.DatahubMessageId, "processed", null, ct);

        logger.LogInformation("Dead letter {DeadLetterId} retried successfully", id);
        return Results.Ok(new { message = "Dead letter reprocessed successfully." });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Dead letter {DeadLetterId} retry failed", id);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// GET /api/messages/stats — message statistics
app.MapGet("/api/messages/stats", async (IMessageRepository repo, CancellationToken ct) =>
{
    var stats = await repo.GetMessageStatsAsync(ct);
    return Results.Ok(stats);
});

// GET /api/messages/conversations — process conversations grouped by correlation ID
app.MapGet("/api/messages/conversations", async (int? page, int? pageSize, IMessageRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetConversationsAsync(p, ps, ct);
    return Results.Ok(result);
});

// GET /api/messages/conversations/{correlationId} — timeline for one conversation
app.MapGet("/api/messages/conversations/{correlationId}", async (string correlationId, IMessageRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetConversationAsync(correlationId, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// GET /api/messages/deliveries — data deliveries grouped by date
app.MapGet("/api/messages/deliveries", async (IMessageRepository repo, CancellationToken ct) =>
{
    var deliveries = await repo.GetDataDeliveriesAsync(ct);
    return Results.Ok(deliveries);
});

// --- Spot Prices (metering) ---

// GET /api/metering/spot-prices — both DK1 and DK2 prices for a single date
app.MapGet("/api/metering/spot-prices", async (
    string? date,
    ISpotPriceRepository repo, CancellationToken ct) =>
{
    DateOnly targetDate;
    if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out var parsed))
    {
        targetDate = parsed;
    }
    else
    {
        // Default to latest date with data (check both areas)
        var latestDk1 = await repo.GetLatestPriceDateAsync("DK1", ct);
        var latestDk2 = await repo.GetLatestPriceDateAsync("DK2", ct);
        var latest = latestDk1.HasValue && latestDk2.HasValue
            ? (latestDk1.Value >= latestDk2.Value ? latestDk1.Value : latestDk2.Value)
            : latestDk1 ?? latestDk2;
        if (!latest.HasValue)
            return Results.Ok(new { date = (DateOnly?)null, totalCount = 0,
                avgPriceDk1 = 0m, minPriceDk1 = 0m, maxPriceDk1 = 0m,
                avgPriceDk2 = 0m, minPriceDk2 = 0m, maxPriceDk2 = 0m,
                items = Array.Empty<object>() });
        targetDate = latest.Value;
    }

    var result = await repo.GetPricesByDateAsync(targetDate, ct);

    return Results.Ok(new
    {
        date = targetDate,
        totalCount = result.TotalCount,
        avgPriceDk1 = result.AvgPriceDk1,
        minPriceDk1 = result.MinPriceDk1,
        maxPriceDk1 = result.MaxPriceDk1,
        avgPriceDk2 = result.AvgPriceDk2,
        minPriceDk2 = result.MinPriceDk2,
        maxPriceDk2 = result.MaxPriceDk2,
        items = result.Items.Select(row => new
        {
            timestamp = row.Timestamp,
            priceDk1 = row.PriceDk1,
            priceDk2 = row.PriceDk2,
            resolution = row.Resolution,
        }),
    });
});

// GET /api/metering/spot-prices/status — unified spot price status
app.MapGet("/api/metering/spot-prices/status", async (ISpotPriceRepository repo, CancellationToken ct) =>
{
    var status = await repo.GetStatusAsync(ct);
    return Results.Ok(new
    {
        latestDate = status.LatestDate,
        lastFetchedAt = status.LastFetchedAt,
        hasTomorrow = status.HasTomorrow,
        status = status.Status,
    });
});

// --- Aconto Prepayments ---

// GET /api/billing/aconto/{gsrn} — aconto prepayment totals for a metering point (from invoice lines)
app.MapGet("/api/billing/aconto/{gsrn}", async (
    string gsrn, DateOnly? from, DateOnly? to,
    IInvoiceRepository repo, CancellationToken ct) =>
{
    var fromDate = from ?? new DateOnly(DateTime.UtcNow.Year, 1, 1);
    var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    var totalPrepaid = await repo.GetAcontoPrepaymentTotalAsync(gsrn, fromDate, toDate, ct);

    return Results.Ok(new
    {
        gsrn,
        from = fromDate,
        to = toDate,
        totalPrepaid,
    });
});

// --- Invoices ---

// GET /api/billing/invoices — paginated invoices with filters and search
app.MapGet("/api/billing/invoices", async (
    Guid? customerId, string? status, string? invoiceType,
    DateOnly? fromDate, DateOnly? toDate, string? search,
    int? page, int? pageSize,
    IInvoiceRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetPagedAsync(customerId, status, invoiceType, fromDate, toDate, search, p, ps, ct);
    return Results.Ok(result);
});

// GET /api/billing/invoices/overdue — all overdue invoices
app.MapGet("/api/billing/invoices/overdue", async (IInvoiceRepository repo, CancellationToken ct) =>
{
    var invoices = await repo.GetOverdueAsync(ct);
    return Results.Ok(invoices);
});

// GET /api/billing/invoices/{id} — invoice detail with lines
app.MapGet("/api/billing/invoices/{id:guid}", async (Guid id, IInvoiceRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetDetailAsync(id, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// POST /api/billing/invoices/{id}/send — draft → sent, assigns invoice number
app.MapPost("/api/billing/invoices/{id:guid}/send", async (Guid id, IInvoiceService service, CancellationToken ct) =>
{
    try
    {
        var invoiceNumber = await service.SendInvoiceAsync(id, ct);
        return Results.Ok(new { invoiceNumber, message = "Invoice sent." });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// POST /api/billing/invoices/{id}/cancel — cancel invoice
app.MapPost("/api/billing/invoices/{id:guid}/cancel", async (Guid id, IInvoiceService service, CancellationToken ct) =>
{
    try
    {
        await service.CancelInvoiceAsync(id, ct);
        return Results.Ok(new { message = "Invoice cancelled." });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// POST /api/billing/invoices/{id}/credit — create credit note
app.MapPost("/api/billing/invoices/{id:guid}/credit", async (Guid id, CreditNoteRequest? request, IInvoiceService service, CancellationToken ct) =>
{
    try
    {
        var creditNote = await service.CreateCreditNoteAsync(id, request?.Notes, ct);
        return Results.Created($"/api/billing/invoices/{creditNote.Id}", creditNote);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// --- Payments ---

// GET /api/billing/payments — paginated payments
app.MapGet("/api/billing/payments", async (
    Guid? customerId, string? status,
    int? page, int? pageSize,
    IPaymentRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetPagedAsync(customerId, status, p, ps, ct);
    return Results.Ok(result);
});

// GET /api/billing/payments/{id} — payment detail with allocations
app.MapGet("/api/billing/payments/{id:guid}", async (Guid id, IPaymentRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetDetailAsync(id, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
});

// POST /api/billing/payments — record payment and auto-match
app.MapPost("/api/billing/payments", async (CreatePaymentRequest request, IPaymentMatchingService service, CancellationToken ct) =>
{
    try
    {
        var payment = await service.RecordAndMatchPaymentAsync(request, ct);
        return Results.Created($"/api/billing/payments/{payment.Id}", payment);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// POST /api/billing/payments/{id}/allocate — manual allocation
app.MapPost("/api/billing/payments/{id:guid}/allocate", async (Guid id, ManualAllocationRequest request, IPaymentMatchingService service, CancellationToken ct) =>
{
    try
    {
        await service.ManualAllocateAsync(id, request.InvoiceId, request.Amount, "backoffice", ct);
        return Results.Ok(new { message = "Payment allocated." });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// POST /api/billing/payments/import — bulk bank file import
app.MapPost("/api/billing/payments/import", async (BankFileImportRequest request, IPaymentMatchingService service, CancellationToken ct) =>
{
    var result = await service.ImportBankFileAsync(request, ct);
    return Results.Ok(result);
});

// --- Customer Balance ---

// GET /api/billing/customers/{id}/balance — customer balance summary
app.MapGet("/api/billing/customers/{id:guid}/balance", async (Guid id, IInvoiceRepository repo, CancellationToken ct) =>
{
    var balance = await repo.GetCustomerBalanceAsync(id, ct);
    return balance is not null ? Results.Ok(balance) : Results.NotFound();
});

// GET /api/billing/customers/{id}/ledger — chronological invoices + payments
app.MapGet("/api/billing/customers/{id:guid}/ledger", async (Guid id, IInvoiceRepository repo, CancellationToken ct) =>
{
    var ledger = await repo.GetCustomerLedgerAsync(id, ct);
    return Results.Ok(ledger);
});

// GET /api/billing/outstanding — all customers with outstanding amounts
app.MapGet("/api/billing/outstanding", async (IInvoiceRepository repo, CancellationToken ct) =>
{
    var outstanding = await repo.GetOutstandingCustomersAsync(ct);
    return Results.Ok(outstanding);
});

// --- Processes ---

// GET /api/processes — processes by status
app.MapGet("/api/processes", async (string? status, IProcessRepository repo, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(status))
        return Results.BadRequest(new { error = "Status filter is required." });

    var processes = await repo.GetByStatusAsync(status, ct);
    return Results.Ok(new
    {
        status,
        count = processes.Count,
        processes = processes.Select(p => new
        {
            p.Id,
            p.ProcessType,
            p.Gsrn,
            p.Status,
            p.EffectiveDate,
            p.DatahubCorrelationId,
        }),
    });
});

// GET /api/processes/{id} — process detail with expected message checklist
app.MapGet("/api/processes/{id:guid}", async (Guid id, IProcessRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetDetailWithChecklistAsync(id, ct);
    if (detail is null) return Results.NotFound();
    return Results.Ok(new
    {
        detail.Id,
        detail.ProcessType,
        detail.Gsrn,
        detail.Status,
        detail.EffectiveDate,
        detail.DatahubCorrelationId,
        detail.CustomerDataReceived,
        detail.TariffDataReceived,
        detail.CreatedAt,
        detail.UpdatedAt,
        expectedMessages = detail.ExpectedMessages.Select(m => new
        {
            m.MessageType,
            m.Received,
            m.ReceivedAt,
            m.Status,
        }),
    });
});

// GET /api/processes/{id}/events — process event timeline
app.MapGet("/api/processes/{id:guid}/events", async (Guid id, IProcessRepository repo, CancellationToken ct) =>
{
    var events = await repo.GetEventsAsync(id, ct);
    return Results.Ok(events.Select(e => new
    {
        e.OccurredAt,
        e.EventType,
        e.Payload,
        e.Source,
    }));
});

// POST /api/processes/end-of-supply — initiate end of supply
app.MapPost("/api/processes/end-of-supply", async (ProcessInitRequest request, IProcessRepository processRepo, IClock clock, CancellationToken ct) =>
{
    var stateMachine = new ProcessStateMachine(processRepo, clock);
    var process = await stateMachine.CreateRequestAsync(request.Gsrn, ProcessTypes.EndOfSupply, request.EffectiveDate, ct);
    return Results.Ok(new { process.Id, process.ProcessType, process.Gsrn, process.Status, process.EffectiveDate });
});

// POST /api/processes/move-out — initiate move-out
app.MapPost("/api/processes/move-out", async (ProcessInitRequest request, IProcessRepository processRepo, IClock clock, CancellationToken ct) =>
{
    var stateMachine = new ProcessStateMachine(processRepo, clock);
    var process = await stateMachine.CreateRequestAsync(request.Gsrn, ProcessTypes.MoveOut, request.EffectiveDate, ct);
    return Results.Ok(new { process.Id, process.ProcessType, process.Gsrn, process.Status, process.EffectiveDate });
});

app.MapFallbackToFile("index.html");

app.Run();

record ProcessInitRequest(string Gsrn, DateOnly EffectiveDate);
record CreditNoteRequest(string? Notes);
record SettlementPreviewRequest(DateOnly PeriodStart, DateOnly PeriodEnd);

class InboundMessageInfoRow
{
    public string DatahubMessageId { get; set; } = null!;
    public string MessageType { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public string QueueName { get; set; } = null!;
}
