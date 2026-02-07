using System.Text.Json;
using DataHub.Settlement.Simulator;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SimulatorState>();

var app = builder.Build();
var state = app.Services.GetRequiredService<SimulatorState>();

// ── OAuth2 token endpoint (fake) ──
app.MapPost("/oauth2/v2.0/token", () =>
{
    return Results.Ok(new
    {
        access_token = $"sim-token-{Guid.NewGuid():N}",
        token_type = "Bearer",
        expires_in = 3600,
    });
});

// ── CIM Queue endpoints ──
app.MapGet("/v1.0/cim/{queue}", (string queue) =>
{
    var msg = state.Peek(queue);
    if (msg is null)
        return Results.NoContent();

    return Results.Ok(new
    {
        MessageId = msg.MessageId,
        MessageType = msg.MessageType,
        CorrelationId = msg.CorrelationId,
        Content = msg.Payload,
    });
});

app.MapDelete("/v1.0/cim/dequeue/{messageId}", (string messageId) =>
{
    return state.Dequeue(messageId)
        ? Results.Ok()
        : Results.NotFound();
});

// ── BRS request endpoints ──
app.MapPost("/v1.0/cim/requestchangeofsupplier", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    state.RecordRequest("requestchangeofsupplier", "/v1.0/cim/requestchangeofsupplier", body);

    var gsrn = ExtractGsrn(body);
    if (gsrn is not null && state.IsGsrnActive(gsrn))
    {
        return Results.Ok(new
        {
            CorrelationId = Guid.NewGuid().ToString(),
            Accepted = false,
            RejectReason = "E16",
            RejectMessage = "Supplier already holds this metering point",
        });
    }

    if (gsrn is not null)
        state.ActivateGsrn(gsrn);

    return Results.Ok(new
    {
        CorrelationId = Guid.NewGuid().ToString(),
        Accepted = true,
    });
});

app.MapPost("/v1.0/cim/requestendofsupply", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    state.RecordRequest("requestendofsupply", "/v1.0/cim/requestendofsupply", body);

    var gsrn = ExtractGsrn(body);
    if (gsrn is not null && !state.IsGsrnActive(gsrn))
    {
        return Results.Ok(new
        {
            CorrelationId = Guid.NewGuid().ToString(),
            Accepted = false,
            RejectReason = "E16",
            RejectMessage = "No active supply for this metering point",
        });
    }

    if (gsrn is not null)
        state.DeactivateGsrn(gsrn);

    return Results.Ok(new
    {
        CorrelationId = Guid.NewGuid().ToString(),
        Accepted = true,
    });
});

app.MapPost("/v1.0/cim/requestcancelchangeofsupplier", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    state.RecordRequest("requestcancelchangeofsupplier", "/v1.0/cim/requestcancelchangeofsupplier", body);
    return Results.Ok(new
    {
        CorrelationId = Guid.NewGuid().ToString(),
        Accepted = true,
    });
});

// ── Admin endpoints ──
app.MapPost("/admin/enqueue", async (HttpRequest request) =>
{
    var body = await JsonSerializer.DeserializeAsync<EnqueueRequest>(request.Body);
    if (body is null)
        return Results.BadRequest("Invalid request body");

    var messageId = state.EnqueueMessage(body.Queue, body.MessageType, body.CorrelationId, body.Payload);
    return Results.Ok(new { MessageId = messageId });
});

app.MapPost("/admin/scenario/{name}", (string name) =>
{
    try
    {
        ScenarioLoader.Load(state, name);
        // Auto-register the default GSRN as active for scenarios that include RSM-007
        if (name is "sunshine" or "full_lifecycle" or "cancellation" or "move_in" or "move_out")
            state.ActivateGsrn("571313100000012345");
        return Results.Ok(new { Scenario = name, Status = "loaded" });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
});

app.MapPost("/admin/activate/{gsrn}", (string gsrn) =>
{
    state.ActivateGsrn(gsrn);
    return Results.Ok(new { Gsrn = gsrn, Status = "active" });
});

app.MapPost("/admin/deactivate/{gsrn}", (string gsrn) =>
{
    state.DeactivateGsrn(gsrn);
    return Results.Ok(new { Gsrn = gsrn, Status = "inactive" });
});

app.MapPost("/admin/reset", () =>
{
    state.Reset();
    return Results.Ok(new { Status = "reset" });
});

app.MapGet("/admin/requests", () =>
{
    return Results.Ok(state.GetRequests());
});

app.MapGet("/", () => "DataHub Settlement Simulator");

app.Run();

static string? ExtractGsrn(string body)
{
    try
    {
        using var doc = JsonDocument.Parse(body);
        // Find the MarketDocument — could be top-level or nested under a BRS-specific key
        JsonElement md = default;
        bool found = false;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name.Contains("MarketDocument", StringComparison.OrdinalIgnoreCase))
            {
                md = prop.Value;
                found = true;
                break;
            }
        }
        if (!found) return null;

        // Find MktActivityRecord
        if (!md.TryGetProperty("MktActivityRecord", out var mar) &&
            !md.TryGetProperty("mktActivityRecord", out mar))
            return null;

        // Find MarketEvaluationPoint
        if (!mar.TryGetProperty("MarketEvaluationPoint", out var mep) &&
            !mar.TryGetProperty("marketEvaluationPoint", out mep))
            return null;

        // Find mRID — could be a string or an object with a "value" property
        if (!mep.TryGetProperty("mRID", out var mrid) &&
            !mep.TryGetProperty("mrid", out mrid))
            return null;

        if (mrid.ValueKind == JsonValueKind.String)
            return mrid.GetString();
        if (mrid.ValueKind == JsonValueKind.Object && mrid.TryGetProperty("value", out var val))
            return val.GetString();
    }
    catch (JsonException)
    {
        // Invalid JSON — can't extract GSRN
    }
    return null;
}

record EnqueueRequest(string Queue, string MessageType, string? CorrelationId, string Payload);

// Make Program accessible for integration tests via WebApplicationFactory
public partial class Program;
