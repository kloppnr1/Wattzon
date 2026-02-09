using Dapper;
using Npgsql;

var connectionString = args.Length > 0
    ? args[0]
    : "Host=localhost;Port=5432;Database=datahub_settlement;Username=settlement;Password=settlement";

Console.WriteLine("🌱 DataHub Settlement Database Seeder");
Console.WriteLine("=====================================");
Console.WriteLine($"Connection: {connectionString.Split(';')[0]}");
Console.WriteLine();

await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync();

// Clear existing seed data
Console.WriteLine("🧹 Cleaning existing seed data...");
await conn.ExecuteAsync("DELETE FROM settlement.settlement_line WHERE metering_point_id LIKE '5713131804%'");
await conn.ExecuteAsync("DELETE FROM settlement.settlement_run WHERE 1=1");
await conn.ExecuteAsync("DELETE FROM settlement.billing_period WHERE 1=1");
await conn.ExecuteAsync("DELETE FROM billing.aconto_payment WHERE 1=1");
await conn.ExecuteAsync("DELETE FROM datahub.dead_letter WHERE 1=1");
await conn.ExecuteAsync("DELETE FROM datahub.outbound_request WHERE 1=1");
await conn.ExecuteAsync("DELETE FROM datahub.inbound_message WHERE 1=1");
await conn.ExecuteAsync("DELETE FROM portfolio.supply_period WHERE gsrn LIKE '5713131804%'");
await conn.ExecuteAsync("DELETE FROM portfolio.contract WHERE gsrn LIKE '5713131804%'");
await conn.ExecuteAsync("DELETE FROM portfolio.metering_point WHERE gsrn LIKE '5713131804%'");
await conn.ExecuteAsync("DELETE FROM portfolio.signup WHERE customer_name LIKE 'Demo%'");
await conn.ExecuteAsync("DELETE FROM lifecycle.process_request WHERE gsrn LIKE '5713131804%'");
await conn.ExecuteAsync("DELETE FROM portfolio.customer WHERE name LIKE 'Demo%'");
Console.WriteLine("✅ Cleaned\n");

// Ensure grid area exists
Console.WriteLine("🌍 Ensuring grid area exists...");
await conn.ExecuteAsync(
    "INSERT INTO portfolio.grid_area (code, grid_operator_gln, grid_operator_name, price_area) VALUES (@Code, @Gln, @Name, @Area) ON CONFLICT (code) DO NOTHING",
    new { Code = "543", Gln = "5790000432752", Name = "Radius Elnet A/S", Area = "DK2" });
Console.WriteLine("  ✓ Grid area 543 (DK2)\n");

// Ensure products exist
Console.WriteLine("📦 Ensuring products exist...");
var productId = await conn.QuerySingleOrDefaultAsync<Guid?>(
    "SELECT id FROM portfolio.product WHERE name = 'Demo Flex Green' LIMIT 1");

if (productId == null || productId == Guid.Empty)
{
    productId = Guid.NewGuid();
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.product (id, name, energy_model, margin_ore_per_kwh, supplement_ore_per_kwh, subscription_kr_per_month) VALUES (@Id, @Name, @Model, @Margin, @Supplement, @Subscription)",
        new { Id = productId, Name = "Demo Flex Green", Model = "spot", Margin = 5.00m, Supplement = 0m, Subscription = 29.00m });
    Console.WriteLine($"  ✓ Created Demo Flex Green");
}
else
{
    Console.WriteLine($"  ✓ Using existing product {productId}");
}
Console.WriteLine();

// Define all signups with customer info
// Per V022 architecture: customers are only created when RSM-007 activates a signup.
// Registered/processing/rejected signups store customer_name/cpr_cvr but have NO customer entity.
Console.WriteLine("📝 Creating signups...");
var signupDefs = new[]
{
    (Name: "Demo Hansen", CprCvr: "1234567890", ContactType: "private", Gsrn: "571313180400000001", Status: "active", Type: "switch"),
    (Name: "Demo Jensen ApS", CprCvr: "12345678", ContactType: "business", Gsrn: "571313180400000002", Status: "active", Type: "switch"),
    (Name: "Demo Nielsen", CprCvr: "0987654321", ContactType: "private", Gsrn: "571313180400000003", Status: "active", Type: "move_in"),
    (Name: "Demo Petersen", CprCvr: "1122334455", ContactType: "private", Gsrn: "571313180400000005", Status: "registered", Type: "switch"),
    (Name: "Demo Andersen", CprCvr: "5544332211", ContactType: "private", Gsrn: "571313180400000006", Status: "processing", Type: "switch"),
    (Name: "Demo Rejected Customer", CprCvr: "9999999999", ContactType: "private", Gsrn: "571313180400000007", Status: "rejected", Type: "switch"),
};

// Only create customer entities for "active" signups (post RSM-007 activation)
Console.WriteLine("👥 Creating customers (only for active signups)...");
var customerMap = new Dictionary<string, Guid>(); // CprCvr → CustomerId
foreach (var s in signupDefs.Where(s => s.Status == "active"))
{
    var customerId = Guid.NewGuid();
    customerMap[s.CprCvr] = customerId;
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.customer (id, name, cpr_cvr, contact_type, status) VALUES (@Id, @Name, @CprCvr, @ContactType, 'active')",
        new { Id = customerId, Name = s.Name, CprCvr = s.CprCvr, ContactType = s.ContactType });
    Console.WriteLine($"  ✓ {s.Name} ({s.ContactType})");
}
Console.WriteLine();

var signupNumber = 1;
foreach (var s in signupDefs)
{
    var effectiveDate = s.Status == "active" ? new DateTime(2025, 1, 1) : DateTime.UtcNow.Date.AddDays(30);

    // Map contact_type: customer table uses 'private'/'business', signup table uses 'person'/'company'
    var signupContactType = s.ContactType == "business" ? "company" : "person";

    // Customer ID only set for "active" signups (after RSM-007 activation)
    var customerId = s.Status == "active" && customerMap.TryGetValue(s.CprCvr, out var cid) ? cid : (Guid?)null;

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.signup (id, signup_number, dar_id, gsrn, customer_id, product_id, type, effective_date, status, created_at, customer_name, customer_cpr_cvr, customer_contact_type) VALUES (@Id, @SignupNum, @DarId, @Gsrn, @CustomerId, @ProductId, @Type, @EffectiveDate, @Status, @Created, @CustomerName, @CustomerCprCvr, @CustomerContactType)",
        new {
            Id = Guid.NewGuid(),
            SignupNum = $"SGN-2026-{signupNumber:D5}",
            DarId = $"0a3f5000-{signupNumber:D4}-62c3-e044-0003ba298018",
            Gsrn = s.Gsrn,
            CustomerId = customerId,
            ProductId = productId,
            Type = s.Type,
            EffectiveDate = effectiveDate,
            Status = s.Status,
            Created = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 30)),
            CustomerName = s.Name,
            CustomerCprCvr = s.CprCvr,
            CustomerContactType = signupContactType
        });

    Console.WriteLine($"  ✓ SGN-2026-{signupNumber:D5} - {s.Name} ({s.Status}){(customerId.HasValue ? "" : " [no customer yet]")}");
    signupNumber++;
}
Console.WriteLine();

// Create metering points (only for active signups — these have completed RSM-007)
Console.WriteLine("⚡ Creating metering points...");
var activeSignups = signupDefs.Where(s => s.Status == "active").ToArray();
var meteringPoints = new[]
{
    (Gsrn: "571313180400000001", CprCvr: "1234567890"),
    (Gsrn: "571313180400000002", CprCvr: "12345678"),
    (Gsrn: "571313180400000003", CprCvr: "0987654321"),
    (Gsrn: "571313180400000004", CprCvr: "1234567890"), // 2nd meter for Demo Hansen
};

foreach (var mp in meteringPoints)
{
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area) VALUES (@Gsrn, 'E17', 'flex', 'connected', '543', '5790000432752', 'DK2')",
        new { mp.Gsrn });
    Console.WriteLine($"  ✓ {mp.Gsrn}");
}
Console.WriteLine();

// Create contracts (only for active signups with customer entities)
Console.WriteLine("📄 Creating contracts...");
foreach (var mp in meteringPoints)
{
    var customerId = customerMap[mp.CprCvr];

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.contract (id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date) VALUES (@Id, @CustomerId, @Gsrn, @ProductId, 'monthly', 'aconto', @Start)",
        new { Id = Guid.NewGuid(), CustomerId = customerId, Gsrn = mp.Gsrn, ProductId = productId, Start = new DateTime(2025, 1, 1) });

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.supply_period (id, gsrn, start_date) VALUES (@Id, @Gsrn, @Start)",
        new { Id = Guid.NewGuid(), Gsrn = mp.Gsrn, Start = new DateTime(2025, 1, 1) });

    Console.WriteLine($"  ✓ Contract for {mp.Gsrn}");
}
Console.WriteLine();

// Create billing periods
Console.WriteLine("📅 Creating billing periods...");
var billingPeriods = new[]
{
    (Id: Guid.NewGuid(), Start: new DateTime(2025, 1, 1), End: new DateTime(2025, 1, 31), Frequency: "monthly"),
    (Id: Guid.NewGuid(), Start: new DateTime(2025, 2, 1), End: new DateTime(2025, 2, 28), Frequency: "monthly"),
    (Id: Guid.NewGuid(), Start: new DateTime(2025, 3, 1), End: new DateTime(2025, 3, 31), Frequency: "monthly"),
};

foreach (var bp in billingPeriods)
{
    await conn.ExecuteAsync(
        "INSERT INTO settlement.billing_period (id, period_start, period_end, frequency, created_at) VALUES (@Id, @Start, @End, @Frequency, @Created)",
        new { bp.Id, bp.Start, bp.End, bp.Frequency, Created = DateTime.UtcNow });
    Console.WriteLine($"  ✓ {bp.Start:yyyy-MM} ({bp.Frequency})");
}
Console.WriteLine();

// Create settlement runs
Console.WriteLine("🔄 Creating settlement runs...");
var settlementRuns = new List<(Guid Id, Guid BillingPeriodId, string GridArea, int Version, string Status, DateTime ExecutedAt, DateTime? CompletedAt)>();

foreach (var bp in billingPeriods.Take(2)) // Only first 2 periods
{
    var runId = Guid.NewGuid();
    var status = "completed";
    var executedAt = bp.End.AddDays(1).Add(TimeSpan.FromHours(2));
    var completedAt = executedAt.AddMinutes(5);

    settlementRuns.Add((runId, bp.Id, "543", 1, status, executedAt, completedAt));

    await conn.ExecuteAsync(
        "INSERT INTO settlement.settlement_run (id, billing_period_id, grid_area_code, version, status, executed_at, completed_at) VALUES (@Id, @BpId, @Grid, @Ver, @Status, @Exec, @Comp)",
        new { Id = runId, BpId = bp.Id, Grid = "543", Ver = 1, Status = status, Exec = executedAt, Comp = completedAt });

    Console.WriteLine($"  ✓ Run for {bp.Start:yyyy-MM} - Status: {status}");
}

// Add one running settlement run
var runningRunId = Guid.NewGuid();
settlementRuns.Add((runningRunId, billingPeriods[2].Id, "543", 1, "running", DateTime.UtcNow, null));
await conn.ExecuteAsync(
    "INSERT INTO settlement.settlement_run (id, billing_period_id, grid_area_code, version, status, executed_at) VALUES (@Id, @BpId, @Grid, @Ver, @Status, @Exec)",
    new { Id = runningRunId, BpId = billingPeriods[2].Id, Grid = "543", Ver = 1, Status = "running", Exec = DateTime.UtcNow });
Console.WriteLine($"  ✓ Run for {billingPeriods[2].Start:yyyy-MM} - Status: running");
Console.WriteLine();

// Create settlement lines (only for completed runs)
Console.WriteLine("💰 Creating settlement lines...");
var chargeTypes = new[] { "energy", "grid_tariff", "system_tariff", "transmission_tariff", "electricity_tax" };
var lineCount = 0;

foreach (var run in settlementRuns.Where(r => r.Status == "completed"))
{
    foreach (var mp in meteringPoints)
    {
        var totalKwh = Random.Shared.Next(300, 500);

        foreach (var chargeType in chargeTypes)
        {
            var amount = chargeType switch
            {
                "energy" => totalKwh * 0.95m,
                "grid_tariff" => totalKwh * 0.28m,
                "system_tariff" => totalKwh * 0.054m,
                "transmission_tariff" => totalKwh * 0.049m,
                "electricity_tax" => totalKwh * 0.008m,
                _ => 0m
            };

            var vat = amount * 0.25m;

            await conn.ExecuteAsync(
                "INSERT INTO settlement.settlement_line (id, settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency) VALUES (@Id, @RunId, @MpId, @Type, @Kwh, @Amount, @Vat, @Currency)",
                new { Id = Guid.NewGuid(), RunId = run.Id, MpId = mp.Gsrn, Type = chargeType, Kwh = (decimal)totalKwh, Amount = Math.Round(amount, 2), Vat = Math.Round(vat, 2), Currency = "DKK" });

            lineCount++;
        }
    }
}
Console.WriteLine($"  ✓ Created {lineCount} settlement lines");
Console.WriteLine();

// Create aconto payments
Console.WriteLine("💳 Creating aconto payments...");
foreach (var mp in meteringPoints.Take(2))
{
    await conn.ExecuteAsync(
        "INSERT INTO billing.aconto_payment (id, gsrn, period_start, period_end, amount, paid_at) VALUES (@Id, @Gsrn, @PeriodStart, @PeriodEnd, @Amount, @PaidAt)",
        new { Id = Guid.NewGuid(), Gsrn = mp.Gsrn, PeriodStart = new DateTime(2025, 1, 1), PeriodEnd = new DateTime(2025, 1, 31), Amount = 500.00m, PaidAt = new DateTime(2025, 1, 15) });

    Console.WriteLine($"  ✓ 500 DKK for {mp.Gsrn}");
}
Console.WriteLine();

// Create inbound messages
Console.WriteLine("📨 Creating inbound messages...");
var messageTypes = new[] { "RSM-007", "RSM-009", "RSM-012", "RSM-014", "RSM-004" };
for (int i = 0; i < 25; i++)
{
    var status = i < 20 ? "processed" : (i < 23 ? "received" : "dead_lettered");
    var messageType = messageTypes[Random.Shared.Next(messageTypes.Length)];
    var msgId = Guid.NewGuid();
    var receivedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 30)).AddHours(-Random.Shared.Next(0, 24));

    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, @Type, @Corr, @Queue, @Status, @Size, @Recv, @Proc)",
        new {
            Id = msgId,
            DhId = $"DH-{Guid.NewGuid()}",
            Type = messageType,
            Corr = $"CORR-{Random.Shared.Next(1000, 9999)}",
            Queue = "cim-001",
            Status = status,
            Size = Random.Shared.Next(500, 2000),
            Recv = receivedAt,
            Proc = status == "processed" ? receivedAt.AddSeconds(Random.Shared.Next(1, 30)) : (DateTime?)null
        });

    // Create dead letter for dead_lettered messages
    if (status == "dead_lettered")
    {
        await conn.ExecuteAsync(
            "INSERT INTO datahub.dead_letter (id, original_message_id, queue_name, error_reason, raw_payload, failed_at, resolved) VALUES (@Id, @OrigId, @Queue, @Error, @Payload::jsonb, @Failed, @Resolved)",
            new {
                Id = Guid.NewGuid(),
                OrigId = msgId.ToString(),
                Queue = "cim-001",
                Error = "Invalid XML structure: missing required element 'mRID'",
                Payload = "{\"raw\":\"<Invalid>XML</Invalid>\"}",
                Failed = receivedAt.AddSeconds(5),
                Resolved = false
            });
    }
}
Console.WriteLine($"  ✓ Created 25 inbound messages (20 processed, 2 dead letters, 3 received)");
Console.WriteLine();

// Create outbound requests
Console.WriteLine("📤 Creating outbound requests...");
var processTypes = new[] { "BRS-001", "BRS-002", "BRS-009", "BRS-010" };
for (int i = 0; i < 18; i++)
{
    var status = i < 14 ? "acknowledged_ok" : (i < 16 ? "acknowledged_error" : "sent");
    var processType = processTypes[Random.Shared.Next(processTypes.Length)];
    var sentAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 20)).AddHours(-Random.Shared.Next(0, 24));

    await conn.ExecuteAsync(
        "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at, error_details) VALUES (@Id, @Type, @Gsrn, @Status, @Corr, @Sent, @Resp, @Error)",
        new {
            Id = Guid.NewGuid(),
            Type = processType,
            Gsrn = meteringPoints[Random.Shared.Next(meteringPoints.Length)].Gsrn,
            Status = status,
            Corr = $"CORR-{Random.Shared.Next(1000, 9999)}",
            Sent = sentAt,
            Resp = status != "sent" ? sentAt.AddMinutes(Random.Shared.Next(1, 30)) : (DateTime?)null,
            Error = status == "acknowledged_error" ? "Business rule validation failed: E86 - Invalid effective date" : null
        });
}
Console.WriteLine($"  ✓ Created 18 outbound requests (14 acknowledged, 2 errors, 2 pending)");
Console.WriteLine();

Console.WriteLine("✅ Database seeding completed!");
Console.WriteLine();
Console.WriteLine("📊 Summary:");
Console.WriteLine($"   • {signupDefs.Length} signups (3 active, 1 registered, 1 processing, 1 rejected)");
Console.WriteLine($"   • {customerMap.Count} customers (only for active signups)");
Console.WriteLine($"   • {meteringPoints.Length} metering points");
Console.WriteLine($"   • {billingPeriods.Length} billing periods");
Console.WriteLine($"   • {settlementRuns.Count} settlement runs (2 completed, 1 running)");
Console.WriteLine($"   • {lineCount} settlement lines");
Console.WriteLine($"   • 25 inbound messages (20 processed, 2 dead letters, 3 received)");
Console.WriteLine($"   • 18 outbound requests (14 OK, 2 errors, 2 pending)");
Console.WriteLine($"   • 2 aconto payments");
Console.WriteLine();
Console.WriteLine("🚀 Go check out the backoffice UI:");
Console.WriteLine("   • http://localhost:5173/signups");
Console.WriteLine("   • http://localhost:5173/billing");
Console.WriteLine("   • http://localhost:5173/messages");
