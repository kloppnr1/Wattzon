using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Infrastructure;
using DataHub.Settlement.Infrastructure.AddressLookup;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.Lifecycle;
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

// GET /api/signups/{id} — full signup detail
app.MapGet("/api/signups/{id:guid}", async (Guid id, ISignupRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetDetailByIdAsync(id, ct);
    return detail is not null ? Results.Ok(detail) : Results.NotFound();
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

app.Run();
