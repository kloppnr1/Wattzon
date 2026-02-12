using System.Text.Json;
using DataHub.Settlement.Simulator;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SimulatorState>();

var app = builder.Build();
var state = app.Services.GetRequiredService<SimulatorState>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Log all incoming requests
app.Use(async (context, next) =>
{
    logger.LogInformation("{Method} {Path}", context.Request.Method, context.Request.Path);
    await next();
});

// Background timer: check pending effectuations every 5 seconds
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(5_000);
        state.FlushReadyEffectuations();
        state.FlushDailyTimeseries();
    }
});

// ── OAuth2 token endpoint (simulated) ──
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
    var correlationId = Guid.NewGuid().ToString();

    if (gsrn is not null && state.IsGsrnActive(gsrn))
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(15_000);
            state.EnqueueMessage("MasterData", "RSM-001", correlationId,
                BuildRsm001ResponseJson(correlationId, false, "E16", "Supplier already holds this metering point"));
        });

        return Results.Ok(new
        {
            CorrelationId = correlationId,
            Accepted = false,
            RejectReason = "E16",
            RejectMessage = "Supplier already holds this metering point",
        });
    }

    if (gsrn is not null)
        state.ActivateGsrn(gsrn);

    var effectiveDateStr = ExtractEffectiveDate(body) ?? "2025-01-01T00:00:00Z";

    var effectiveDate = DateOnly.TryParse(effectiveDateStr.Split('T')[0], out var ed)
        ? ed : DateOnly.FromDateTime(DateTime.UtcNow);
    var gsrnForCapture = gsrn ?? "571313100000012345";

    // RSM-001 response (acknowledgment) after 15s delay, then schedule effectuation
    _ = Task.Run(async () =>
    {
        await Task.Delay(15_000);
        state.EnqueueMessage("MasterData", "RSM-001", correlationId,
            BuildRsm001ResponseJson(correlationId, true));

        // Schedule RSM-022 after RSM-001 is on the queue so the poller processes them in order
        state.ScheduleEffectuation(gsrnForCapture, correlationId, effectiveDate);
    });

    // RSM-028 (customer data, no CPR per BRS-001 spec) after 16s delay
    _ = Task.Run(async () =>
    {
        await Task.Delay(16_000);
        state.EnqueueMessage("MasterData", "RSM-028", correlationId,
            ScenarioLoader.BuildRsm028Json(gsrnForCapture, "Simulated Customer", "0000000000", includeCpr: false));
    });

    // RSM-031 (price attachments) after 17s delay
    _ = Task.Run(async () =>
    {
        await Task.Delay(17_000);
        state.EnqueueMessage("MasterData", "RSM-031", correlationId,
            ScenarioLoader.BuildRsm031Json(gsrnForCapture, effectiveDateStr));
    });

    return Results.Ok(new
    {
        CorrelationId = correlationId,
        Accepted = true,
    });
});

app.MapPost("/v1.0/cim/requestendofsupply", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    state.RecordRequest("requestendofsupply", "/v1.0/cim/requestendofsupply", body);

    var gsrn = ExtractGsrn(body);
    var correlationId = Guid.NewGuid().ToString();

    if (gsrn is not null && !state.IsGsrnActive(gsrn))
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(15_000);
            state.EnqueueMessage("MasterData", "RSM-005", correlationId,
                ScenarioLoader.BuildRsm005ResponseJson(correlationId, false, "E16", "No active supply for this metering point"));
        });

        return Results.Ok(new
        {
            CorrelationId = correlationId,
            Accepted = false,
            RejectReason = "E16",
            RejectMessage = "No active supply for this metering point",
        });
    }

    if (gsrn is not null)
        state.DeactivateGsrn(gsrn);

    // RSM-005 response (no RSM-022 for end-of-supply) after 15s delay
    _ = Task.Run(async () =>
    {
        await Task.Delay(15_000);
        state.EnqueueMessage("MasterData", "RSM-005", correlationId,
            ScenarioLoader.BuildRsm005ResponseJson(correlationId, true));
    });

    return Results.Ok(new
    {
        CorrelationId = correlationId,
        Accepted = true,
    });
});

app.MapPost("/v1.0/cim/requestcancelchangeofsupplier", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    state.RecordRequest("requestcancelchangeofsupplier", "/v1.0/cim/requestcancelchangeofsupplier", body);

    // Reuse the original BRS-001 correlation ID — pre-effective-date cancel is part of the same process
    var correlationId = ExtractOriginalTransactionId(body) ?? Guid.NewGuid().ToString();

    // Cancel pending effectuation so RSM-007 doesn't fire after cancel
    var gsrn = ExtractGsrn(body);
    if (gsrn is not null)
        state.CancelEffectuation(gsrn);

    _ = Task.Run(async () =>
    {
        await Task.Delay(15_000);
        state.EnqueueMessage("MasterData", "RSM-001", correlationId,
            BuildRsm001ResponseJson(correlationId, true));
    });

    return Results.Ok(new
    {
        CorrelationId = correlationId,
        Accepted = true,
    });
});

// ── RSM-027: Customer data update (post-switch) ──
app.MapPost("/v1.0/cim/requestcustomerdataupdate", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    state.RecordRequest("requestcustomerdataupdate", "/v1.0/cim/requestcustomerdataupdate", body);

    var correlationId = Guid.NewGuid().ToString();

    return Results.Ok(new
    {
        CorrelationId = correlationId,
        Accepted = true,
    });
});

// ── BRS-044: Forced supplier switch (admin-initiated) ──
app.MapPost("/admin/brs044", async (HttpRequest request) =>
{
    var body = await JsonSerializer.DeserializeAsync<Brs044Request>(request.Body);
    if (body is null)
        return Results.BadRequest("Invalid request body");

    var correlationId = Guid.NewGuid().ToString();
    var gsrn = body.Gsrn;
    var effectiveDateStr = body.EffectiveDate;
    var customerName = body.CustomerName ?? "Simulated Customer";

    state.ActivateGsrn(gsrn);

    // RSM-004/D31 immediately (transfer notification)
    state.EnqueueMessage("MasterData", "RSM-004", correlationId,
        ScenarioLoader.BuildRsm004D31Json(gsrn, effectiveDateStr));

    // RSM-022 (master data) — deferred until effective date, like real DataHub
    var brs044EffectiveDate = DateOnly.TryParse(effectiveDateStr.Split('T')[0], out var brs044Ed)
        ? brs044Ed : DateOnly.FromDateTime(DateTime.UtcNow);
    state.ScheduleEffectuation(gsrn, correlationId, brs044EffectiveDate);

    // RSM-028 after 2s (customer data, no CPR)
    _ = Task.Run(async () =>
    {
        await Task.Delay(2_000);
        state.EnqueueMessage("MasterData", "RSM-028", correlationId,
            ScenarioLoader.BuildRsm028Json(gsrn, customerName, "0000000000", includeCpr: false));
    });

    // RSM-031 after 3s (price attachments)
    _ = Task.Run(async () =>
    {
        await Task.Delay(3_000);
        state.EnqueueMessage("MasterData", "RSM-031", correlationId,
            ScenarioLoader.BuildRsm031Json(gsrn, effectiveDateStr));
    });

    // RSM-012 after 4s (metering data — only for retroactive switches)
    if (DateOnly.TryParse(effectiveDateStr.Split('T')[0], out var ed) && ed < DateOnly.FromDateTime(DateTime.UtcNow))
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(4_000);
            var start = new DateTimeOffset(ed.Year, ed.Month, ed.Day, 0, 0, 0, TimeSpan.Zero);
            var end = DateTimeOffset.UtcNow.Date;
            var hours = (int)(end - start).TotalHours;
            if (hours > 0)
            {
                state.EnqueueMessage("Timeseries", "RSM-012", correlationId,
                    ScenarioLoader.BuildRsm012Json(gsrn, start, new DateTimeOffset(end, TimeSpan.Zero), hours));
            }
        });
    }

    return Results.Ok(new
    {
        CorrelationId = correlationId,
        Gsrn = gsrn,
        EffectiveDate = effectiveDateStr,
        Status = "initiated",
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
        if (name is "sunshine" or "full_lifecycle" or "cancellation" or "move_in" or "move_out" or "auto_cancel" or "forced_switch")
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

app.MapGet("/admin/effectuations", () =>
{
    return Results.Ok(state.GetPendingEffectuations().Select(e => new
    {
        e.Gsrn,
        e.CorrelationId,
        EffectiveDate = e.EffectiveDate.ToString("yyyy-MM-dd"),
        e.Enqueued,
    }));
});

app.MapGet("/", () => "DataHub Settlement Simulator");

app.Run();

static string BuildRsm001ResponseJson(string correlationId, bool accepted, string? rejectCode = null, string? rejectMessage = null)
{
    if (accepted)
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = correlationId,
                MktActivityRecord = new
                {
                    status = new { value = "A01" },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }
    else
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = correlationId,
                MktActivityRecord = new
                {
                    status = new { value = "A02" },
                    Reason = new { code = rejectCode ?? "E16", text = rejectMessage ?? "Rejected" },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }
}

static string? ExtractEffectiveDate(string body)
{
    try
    {
        using var doc = JsonDocument.Parse(body);
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

        if (!md.TryGetProperty("MktActivityRecord", out var mar) &&
            !md.TryGetProperty("mktActivityRecord", out mar))
            return null;

        // Try start date (switch/move-in)
        if (mar.TryGetProperty("start_DateAndOrTime", out var startDt) &&
            startDt.TryGetProperty("dateTime", out var startVal))
            return startVal.GetString();

        // Try end date (end-of-supply)
        if (mar.TryGetProperty("end_DateAndOrTime", out var endDt) &&
            endDt.TryGetProperty("dateTime", out var endVal))
            return endVal.GetString();
    }
    catch (JsonException)
    {
        // Invalid JSON
    }
    return null;
}

static string? ExtractOriginalTransactionId(string body)
{
    try
    {
        using var doc = JsonDocument.Parse(body);
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

        if (!md.TryGetProperty("MktActivityRecord", out var mar) &&
            !md.TryGetProperty("mktActivityRecord", out mar))
            return null;

        if (mar.TryGetProperty("originalTransactionIDReference_MktActivityRecord.mRID", out var otid))
            return otid.GetString();
        if (mar.TryGetProperty("originalTransactionID", out var ot))
            return ot.GetString();
    }
    catch (JsonException) { }
    return null;
}

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
record Brs044Request(string Gsrn, string EffectiveDate, string? CustomerName);

// Make Program accessible for integration tests via WebApplicationFactory
public partial class Program;
