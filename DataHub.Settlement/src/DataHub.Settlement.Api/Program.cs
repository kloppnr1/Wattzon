using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Infrastructure;
using DataHub.Settlement.Infrastructure.AddressLookup;
using DataHub.Settlement.Infrastructure.Billing;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Portfolio;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SettlementDb")
    ?? "Host=localhost;Port=5432;Database=datahub_settlement;Username=settlement;Password=settlement";

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
builder.Services.AddSingleton<IOnboardingService, OnboardingService>();
builder.Services.AddSingleton<IBillingRepository>(new BillingRepository(connectionString));
builder.Services.AddSingleton<ISpotPriceRepository>(new SpotPriceRepository(connectionString));
builder.Services.AddSingleton<IMessageRepository>(new MessageRepository(connectionString));

var app = builder.Build();

// Run database migrations
var migrationLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrator");
DatabaseMigrator.Migrate(connectionString, migrationLogger);

app.UseCors();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

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

// GET /api/address/{darId} — resolve DAR ID to GSRN(s)
app.MapGet("/api/address/{darId}", async (string darId, IAddressLookupClient addressLookup, CancellationToken ct) =>
{
    var result = await addressLookup.LookupByDarIdAsync(darId, ct);
    return Results.Ok(result.MeteringPoints.Select(mp => new
    {
        mp.Gsrn,
        mp.Type,
        grid_area_code = mp.GridAreaCode,
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

// GET /api/customers/{id} — customer detail with contracts and metering points
app.MapGet("/api/customers/{id:guid}", async (Guid id, IPortfolioRepository repo, CancellationToken ct) =>
{
    var customer = await repo.GetCustomerAsync(id, ct);
    if (customer is null) return Results.NotFound();

    var contracts = await repo.GetContractsForCustomerAsync(id, ct);
    var meteringPoints = await repo.GetMeteringPointsForCustomerAsync(id, ct);

    return Results.Ok(new
    {
        customer.Id,
        customer.Name,
        customer.CprCvr,
        customer.ContactType,
        customer.Status,
        Contracts = contracts,
        MeteringPoints = meteringPoints,
    });
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

// GET /api/billing/runs — paginated settlement runs (optional billing period filter)
app.MapGet("/api/billing/runs", async (Guid? billingPeriodId, int? page, int? pageSize, IBillingRepository repo, CancellationToken ct) =>
{
    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var result = await repo.GetSettlementRunsAsync(billingPeriodId, p, ps, ct);
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

// GET /api/billing/customers/{id}/summary — customer billing summary
app.MapGet("/api/billing/customers/{id:guid}/summary", async (Guid id, IBillingRepository repo, CancellationToken ct) =>
{
    var summary = await repo.GetCustomerBillingAsync(id, ct);
    return summary is not null ? Results.Ok(summary) : Results.NotFound();
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

// GET /api/metering/spot-prices — spot prices with date range filter + pagination
app.MapGet("/api/metering/spot-prices", async (
    string? priceArea, string? from, string? to,
    int? page, int? pageSize,
    ISpotPriceRepository repo, CancellationToken ct) =>
{
    var area = priceArea ?? "DK1";
    var toDate = !string.IsNullOrEmpty(to) && DateOnly.TryParse(to, out var toParsed) ? toParsed : DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
    var fromDate = !string.IsNullOrEmpty(from) && DateOnly.TryParse(from, out var fromParsed) ? fromParsed : toDate.AddDays(-7);
    var start = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    var end = toDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    var p = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 200, 1, 500);

    var result = await repo.GetPricesPagedAsync(area, start, end, p, ps, ct);
    var totalPages = (int)Math.Ceiling((double)result.TotalCount / ps);

    return Results.Ok(new
    {
        priceArea = area,
        from = fromDate,
        to = toDate,
        totalCount = result.TotalCount,
        page = p,
        pageSize = ps,
        totalPages,
        avgPrice = result.AvgPrice,
        minPrice = result.MinPrice,
        maxPrice = result.MaxPrice,
        items = result.Items.Select(price => new
        {
            timestamp = price.Timestamp,
            priceArea = price.PriceArea,
            pricePerKwh = price.PricePerKwh,
            resolution = price.Resolution,
        }),
    });
});

// GET /api/metering/spot-prices/latest — latest price date per area
app.MapGet("/api/metering/spot-prices/latest", async (ISpotPriceRepository repo, CancellationToken ct) =>
{
    var dk1 = await repo.GetLatestPriceDateAsync("DK1", ct);
    var dk2 = await repo.GetLatestPriceDateAsync("DK2", ct);
    return Results.Ok(new { dk1, dk2 });
});

app.Run();
