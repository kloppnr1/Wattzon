using Dapper;
using Npgsql;

var connectionString = args.Length > 0
    ? args[0]
    : "Host=localhost;Port=5432;Database=datahub_settlement;Username=settlement;Password=settlement";

Console.WriteLine("DataHub Settlement Database Seeder (10x)");
Console.WriteLine("=========================================");
Console.WriteLine($"Connection: {connectionString.Split(';')[0]}");
Console.WriteLine();

await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync();

// ── Cleanup ──────────────────────────────────────────────────────────
Console.WriteLine("Cleaning existing seed data...");
await conn.ExecuteAsync("DELETE FROM settlement.settlement_line");
await conn.ExecuteAsync("DELETE FROM settlement.settlement_run");
await conn.ExecuteAsync("DELETE FROM settlement.billing_period");
await conn.ExecuteAsync("DELETE FROM billing.aconto_payment");
await conn.ExecuteAsync("DELETE FROM datahub.dead_letter");
await conn.ExecuteAsync("DELETE FROM datahub.processed_message_id");
await conn.ExecuteAsync("DELETE FROM datahub.outbound_request");
await conn.ExecuteAsync("DELETE FROM datahub.inbound_message");
await conn.ExecuteAsync("DELETE FROM portfolio.supply_period");
await conn.ExecuteAsync("DELETE FROM portfolio.contract");
await conn.ExecuteAsync("DELETE FROM portfolio.metering_point");
await conn.ExecuteAsync("DELETE FROM portfolio.signup");
await conn.ExecuteAsync("DELETE FROM lifecycle.process_event");
await conn.ExecuteAsync("DELETE FROM lifecycle.process_request");
await conn.ExecuteAsync("DELETE FROM portfolio.customer");
Console.WriteLine("  Done\n");

var rng = new Random(42); // deterministic seed for reproducibility

// ══════════════════════════════════════════════════════════════════════
// Phase 1: Reference data (grid areas, products)
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 1: Reference data...");

var gridAreas = new[]
{
    (Code: "543", Gln: "5790000432752", Name: "Radius Elnet A/S", PriceArea: "DK2"),
    (Code: "344", Gln: "5790001089030", Name: "N1 A/S", PriceArea: "DK1"),
    (Code: "740", Gln: "5790000705689", Name: "Cerius A/S", PriceArea: "DK1"),
};

foreach (var ga in gridAreas)
{
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.grid_area (code, grid_operator_gln, grid_operator_name, price_area) VALUES (@Code, @Gln, @Name, @Area) ON CONFLICT (code) DO NOTHING",
        new { ga.Code, ga.Gln, ga.Name, Area = ga.PriceArea });
}
Console.WriteLine($"  {gridAreas.Length} grid areas");

var products = new[]
{
    (Name: "Spot Green", Model: "spot", Margin: 4.50m, Supp: 1.00m, Sub: 29.00m, Desc: "100% green electricity at spot prices", Green: true, Order: 1),
    (Name: "Spot Standard", Model: "spot", Margin: 3.00m, Supp: 0m, Sub: 19.00m, Desc: "Standard electricity at spot prices", Green: false, Order: 2),
    (Name: "Fixed Price", Model: "fixed_price", Margin: 8.00m, Supp: 0m, Sub: 39.00m, Desc: "Fixed price for 12 months", Green: false, Order: 3),
    (Name: "Mixed Green", Model: "mixed", Margin: 6.00m, Supp: 2.00m, Sub: 35.00m, Desc: "Blended spot + fixed with green certificates", Green: true, Order: 4),
};

var productIds = new List<Guid>();
foreach (var p in products)
{
    var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(
        "SELECT id FROM portfolio.product WHERE name = @Name LIMIT 1", new { p.Name });

    if (existingId.HasValue && existingId.Value != Guid.Empty)
    {
        productIds.Add(existingId.Value);
    }
    else
    {
        var id = Guid.NewGuid();
        productIds.Add(id);
        await conn.ExecuteAsync(
            "INSERT INTO portfolio.product (id, name, energy_model, margin_ore_per_kwh, supplement_ore_per_kwh, subscription_kr_per_month, description, green_energy, display_order) VALUES (@Id, @Name, @Model, @Margin, @Supp, @Sub, @Desc, @Green, @Order)",
            new { Id = id, p.Name, p.Model, p.Margin, p.Supp, p.Sub, p.Desc, p.Green, p.Order });
    }
}
Console.WriteLine($"  {products.Length} products\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 2: Build signup definitions
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 2: Building signup + customer definitions...");

// Danish first + last names for realistic data
var firstNames = new[] { "Anders", "Mette", "Lars", "Sofie", "Jens", "Camilla", "Mikkel", "Louise", "Henrik", "Anne",
    "Peter", "Maria", "Rasmus", "Ida", "Christian", "Emma", "Thomas", "Julie", "Martin", "Katrine",
    "Nikolaj", "Sara", "Kasper", "Cecilie", "Jonas", "Nanna", "Simon", "Laura", "Frederik", "Astrid" };
var lastNames = new[] { "Hansen", "Jensen", "Nielsen", "Andersen", "Pedersen", "Christensen", "Larsen", "Sorensen",
    "Rasmussen", "Petersen", "Madsen", "Kristensen", "Olsen", "Thomsen", "Poulsen", "Johansen",
    "Knudsen", "Mortensen", "Moller", "Jakobsen" };
var companyNames = new[] { "ApS", "A/S", "I/S", "K/S" };
var companyPrefixes = new[] { "Nordic", "Dansk", "Green", "Sol", "Vind", "El", "Smart", "Digital", "Eco", "Nord" };
var companySuffixes = new[] { "Energy", "Tech", "Solutions", "Service", "Group", "Power", "Systems", "Trading", "Design", "Consult" };

// Grid area distribution: 543=~48%, 344=~32%, 740=~20%
string PickGridArea(int index) => (index % 100) switch
{
    < 48 => "543",
    < 80 => "344",
    _ => "740"
};

string GridGln(string code) => code switch { "543" => "5790000432752", "344" => "5790001089030", _ => "5790000705689" };
string GridPriceArea(string code) => code == "543" ? "DK2" : "DK1";

// Generate GSRN numbers: 571313180400000001 pattern, unique per signup
string MakeGsrn(int n) => $"57131318040{n:D7}";

// Signup definitions
var signupDefs = new List<(string Name, string CprCvr, string ContactType, string SignupContactType, string Gsrn, string Status, string Type, string GridArea, int Index)>();
int gsrnCounter = 1;

// 200 active signups
for (int i = 0; i < 200; i++)
{
    bool isBusiness = i % 6 == 0; // ~33 business
    string name, cprCvr, contactType, signupContactType;
    if (isBusiness)
    {
        name = $"{companyPrefixes[rng.Next(companyPrefixes.Length)]} {companySuffixes[rng.Next(companySuffixes.Length)]} {companyNames[rng.Next(companyNames.Length)]}";
        cprCvr = $"{10000000 + i:D8}";
        contactType = "business";
        signupContactType = "company";
    }
    else
    {
        name = $"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}";
        cprCvr = $"{1000000000L + i:D10}";
        contactType = "private";
        signupContactType = "person";
    }
    var ga = PickGridArea(i);
    var type = i % 5 == 0 ? "move_in" : "switch";
    signupDefs.Add((name, cprCvr, contactType, signupContactType, MakeGsrn(gsrnCounter++), "active", type, ga, i));
}

// 10 processing signups
for (int i = 0; i < 10; i++)
{
    var name = $"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}";
    var cprCvr = $"{2000000000L + i:D10}";
    var ga = PickGridArea(200 + i);
    signupDefs.Add((name, cprCvr, "private", "person", MakeGsrn(gsrnCounter++), "processing", "switch", ga, 200 + i));
}

// 8 registered signups
for (int i = 0; i < 8; i++)
{
    var name = $"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}";
    var cprCvr = $"{3000000000L + i:D10}";
    var ga = PickGridArea(210 + i);
    signupDefs.Add((name, cprCvr, "private", "person", MakeGsrn(gsrnCounter++), "registered", "switch", ga, 210 + i));
}

// 7 rejected signups
for (int i = 0; i < 7; i++)
{
    var name = $"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}";
    var cprCvr = $"{4000000000L + i:D10}";
    var ga = PickGridArea(218 + i);
    signupDefs.Add((name, cprCvr, "private", "person", MakeGsrn(gsrnCounter++), "rejected", "switch", ga, 218 + i));
}

// 5 cancelled signups
for (int i = 0; i < 5; i++)
{
    var name = $"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}";
    var cprCvr = $"{5000000000L + i:D10}";
    var ga = PickGridArea(225 + i);
    signupDefs.Add((name, cprCvr, "private", "person", MakeGsrn(gsrnCounter++), "cancelled", "switch", ga, 225 + i));
}

Console.WriteLine($"  {signupDefs.Count} signup definitions built");
Console.WriteLine($"    {signupDefs.Count(s => s.Status == "active")} active, {signupDefs.Count(s => s.Status == "processing")} processing, {signupDefs.Count(s => s.Status == "registered")} registered, {signupDefs.Count(s => s.Status == "rejected")} rejected, {signupDefs.Count(s => s.Status == "cancelled")} cancelled\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 2b: Create customers
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 2b: Creating customers...");

// Customers for active signups (dedup by CPR/CVR)
var customerMap = new Dictionary<string, Guid>(); // CprCvr -> CustomerId
foreach (var s in signupDefs.Where(s => s.Status == "active"))
{
    if (customerMap.ContainsKey(s.CprCvr)) continue;
    var customerId = Guid.NewGuid();
    customerMap[s.CprCvr] = customerId;
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.customer (id, name, cpr_cvr, contact_type, status) VALUES (@Id, @Name, @CprCvr, @ContactType, 'active')",
        new { Id = customerId, Name = s.Name, CprCvr = s.CprCvr, ContactType = s.ContactType });
}
Console.WriteLine($"  {customerMap.Count} customers from active signups");

// 260 "existing portfolio" customers (onboarded before system tracked signups)
int existingCustomerCount = 0;
for (int i = 0; i < 260; i++)
{
    bool isBusiness = i % 4 == 0; // ~65 business
    string name, cprCvr, contactType;
    if (isBusiness)
    {
        name = $"{companyPrefixes[rng.Next(companyPrefixes.Length)]} {companySuffixes[rng.Next(companySuffixes.Length)]} {companyNames[rng.Next(companyNames.Length)]}";
        cprCvr = $"{60000000 + i:D8}";
        contactType = "business";
    }
    else
    {
        name = $"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}";
        cprCvr = $"{6000000000L + i:D10}";
        contactType = "private";
    }

    if (customerMap.ContainsKey(cprCvr)) continue;

    var customerId = Guid.NewGuid();
    customerMap[cprCvr] = customerId;
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.customer (id, name, cpr_cvr, contact_type, status) VALUES (@Id, @Name, @CprCvr, @ContactType, 'active')",
        new { Id = customerId, Name = name, CprCvr = cprCvr, ContactType = contactType });
    existingCustomerCount++;
}
Console.WriteLine($"  {existingCustomerCount} existing portfolio customers");
Console.WriteLine($"  {customerMap.Count} total customers\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 3: Signups + process requests with correlation IDs
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 3: Creating signups + process requests...");

var processRequestIds = new Dictionary<int, Guid>(); // signup index -> process_request_id
var signupIds = new Dictionary<int, Guid>(); // signup index -> signup_id
int signupNumber = 1;

foreach (var s in signupDefs)
{
    var signupId = Guid.NewGuid();
    signupIds[s.Index] = signupId;
    var effectiveDate = s.Status == "active" ? new DateTime(2025, 1, 1) : DateTime.UtcNow.Date.AddDays(30);
    var customerId = s.Status == "active" && customerMap.TryGetValue(s.CprCvr, out var cid) ? cid : (Guid?)null;

    // Create process request for non-registered signups
    Guid? processRequestId = null;
    if (s.Status != "registered")
    {
        processRequestId = Guid.NewGuid();
        processRequestIds[s.Index] = processRequestId.Value;
        var corrId = $"CORR-SEED-{s.Index:D4}";

        var processStatus = s.Status switch
        {
            "active" => "completed",
            "processing" => "effectuation_pending",
            "rejected" => "rejected",
            "cancelled" => "cancelled",
            _ => "pending"
        };

        var processType = s.Type == "move_in" ? "move_in" : "supplier_switch";

        await conn.ExecuteAsync(
            "INSERT INTO lifecycle.process_request (id, process_type, gsrn, status, effective_date, datahub_correlation_id, requested_at, completed_at, created_at) VALUES (@Id, @ProcessType, @Gsrn, @Status, @EffectiveDate, @CorrId, @Requested, @Completed, @Created)",
            new
            {
                Id = processRequestId.Value,
                ProcessType = processType,
                Gsrn = s.Gsrn,
                Status = processStatus,
                EffectiveDate = effectiveDate,
                CorrId = corrId,
                Requested = DateTime.UtcNow.AddDays(-rng.Next(10, 60)),
                Completed = processStatus == "completed" ? DateTime.UtcNow.AddDays(-rng.Next(1, 10)) : (DateTime?)null,
                Created = DateTime.UtcNow.AddDays(-rng.Next(10, 60))
            });
    }

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.signup (id, signup_number, dar_id, gsrn, customer_id, product_id, process_request_id, type, effective_date, status, created_at, customer_name, customer_cpr_cvr, customer_contact_type) VALUES (@Id, @SignupNum, @DarId, @Gsrn, @CustomerId, @ProductId, @ProcessRequestId, @Type, @EffectiveDate, @Status, @Created, @CustomerName, @CustomerCprCvr, @CustomerContactType)",
        new
        {
            Id = signupId,
            SignupNum = $"SGN-2025-{signupNumber:D5}",
            DarId = $"0a3f5000-{signupNumber:D4}-62c3-e044-0003ba298018",
            Gsrn = s.Gsrn,
            CustomerId = customerId,
            ProductId = productIds[s.Index % productIds.Count],
            ProcessRequestId = processRequestId,
            Type = s.Type,
            EffectiveDate = effectiveDate,
            Status = s.Status,
            Created = DateTime.UtcNow.AddDays(-rng.Next(10, 60)),
            CustomerName = s.Name,
            CustomerCprCvr = s.CprCvr,
            CustomerContactType = s.SignupContactType
        });

    signupNumber++;
}
Console.WriteLine($"  {signupDefs.Count} signups created");
Console.WriteLine($"  {processRequestIds.Count} process requests created\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 4: Portfolio (metering points, contracts, supply periods)
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 4: Creating portfolio entities...");

// Active signups get metering points, contracts, supply periods
int mpCount = 0;
var activeMeteringPoints = new List<string>();

foreach (var s in signupDefs.Where(s => s.Status == "active"))
{
    var ga = s.GridArea;
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area) VALUES (@Gsrn, 'E17', 'flex', 'connected', @GridArea, @Gln, @PriceArea)",
        new { Gsrn = s.Gsrn, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga) });

    var customerId = customerMap[s.CprCvr];
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.contract (id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date) VALUES (@Id, @CustomerId, @Gsrn, @ProductId, 'monthly', 'aconto', @Start)",
        new { Id = Guid.NewGuid(), CustomerId = customerId, Gsrn = s.Gsrn, ProductId = productIds[s.Index % productIds.Count], Start = new DateTime(2025, 1, 1) });

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.supply_period (id, gsrn, start_date) VALUES (@Id, @Gsrn, @Start)",
        new { Id = Guid.NewGuid(), Gsrn = s.Gsrn, Start = new DateTime(2025, 1, 1) });

    activeMeteringPoints.Add(s.Gsrn);
    mpCount++;
}

// Existing portfolio customers also get metering points, contracts, supply periods (~300 from existing customers)
var existingCustomerList = customerMap.Where(kvp => !signupDefs.Any(s => s.Status == "active" && s.CprCvr == kvp.Key)).Take(300).ToList();
foreach (var (cprCvr, customerId) in existingCustomerList)
{
    var gsrn = MakeGsrn(gsrnCounter++);
    var gaIdx = gsrnCounter % 100;
    var ga = gaIdx < 48 ? "543" : gaIdx < 80 ? "344" : "740";

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area) VALUES (@Gsrn, 'E17', 'flex', 'connected', @GridArea, @Gln, @PriceArea)",
        new { Gsrn = gsrn, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga) });

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.contract (id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date) VALUES (@Id, @CustomerId, @Gsrn, @ProductId, 'monthly', 'aconto', @Start)",
        new { Id = Guid.NewGuid(), CustomerId = customerId, Gsrn = gsrn, ProductId = productIds[rng.Next(productIds.Count)], Start = new DateTime(2025, 1, 1) });

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.supply_period (id, gsrn, start_date) VALUES (@Id, @Gsrn, @Start)",
        new { Id = Guid.NewGuid(), Gsrn = gsrn, Start = new DateTime(2025, 1, 1) });

    activeMeteringPoints.Add(gsrn);
    mpCount++;
}

// 10 multi-MP customers (2nd metering point)
var multiMpCustomers = signupDefs.Where(s => s.Status == "active").Take(10).ToList();
foreach (var s in multiMpCustomers)
{
    var gsrn2 = MakeGsrn(gsrnCounter++);
    var ga = s.GridArea;

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area) VALUES (@Gsrn, 'E17', 'flex', 'connected', @GridArea, @Gln, @PriceArea)",
        new { Gsrn = gsrn2, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga) });

    var customerId = customerMap[s.CprCvr];
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.contract (id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date) VALUES (@Id, @CustomerId, @Gsrn, @ProductId, 'monthly', 'aconto', @Start)",
        new { Id = Guid.NewGuid(), CustomerId = customerId, Gsrn = gsrn2, ProductId = productIds[s.Index % productIds.Count], Start = new DateTime(2025, 1, 1) });

    await conn.ExecuteAsync(
        "INSERT INTO portfolio.supply_period (id, gsrn, start_date) VALUES (@Id, @Gsrn, @Start)",
        new { Id = Guid.NewGuid(), Gsrn = gsrn2, Start = new DateTime(2025, 1, 1) });

    activeMeteringPoints.Add(gsrn2);
    mpCount++;
}

// 5 disconnected metering points
for (int i = 0; i < 5; i++)
{
    var gsrn = MakeGsrn(gsrnCounter++);
    var ga = PickGridArea(i);
    await conn.ExecuteAsync(
        "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area, deactivated_at) VALUES (@Gsrn, 'E17', 'flex', 'disconnected', @GridArea, @Gln, @PriceArea, @DeactivatedAt)",
        new { Gsrn = gsrn, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga), DeactivatedAt = DateTime.UtcNow.AddDays(-rng.Next(30, 180)) });
    mpCount++;
}

Console.WriteLine($"  {mpCount} metering points ({activeMeteringPoints.Count} active + 5 disconnected)");
Console.WriteLine($"  {activeMeteringPoints.Count} contracts");
Console.WriteLine($"  {activeMeteringPoints.Count} supply periods\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 5: Process-linked messages (BRS outbound + RSM inbound)
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 5: Creating process-linked messages...");

int outboundCount = 0;
int inboundCount = 0;

foreach (var s in signupDefs.Where(s => s.Status == "active"))
{
    var corrId = $"CORR-SEED-{s.Index:D4}";
    var rsmType = s.Type == "move_in" ? "RSM-001" : "RSM-001";
    var sentAt = DateTime.UtcNow.AddDays(-rng.Next(15, 55));

    // 1. RSM outbound request
    await conn.ExecuteAsync(
        "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at) VALUES (@Id, @Type, @Gsrn, 'acknowledged_ok', @CorrId, @Sent, @Resp)",
        new { Id = Guid.NewGuid(), Type = rsmType, Gsrn = s.Gsrn, CorrId = corrId, Sent = sentAt, Resp = sentAt.AddMinutes(rng.Next(1, 15)) });
    outboundCount++;

    // 2. RSM-001 acknowledgement inbound
    var rsm009At = sentAt.AddMinutes(rng.Next(2, 30));
    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-001', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
        new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(800, 1500), Recv = rsm009At, Proc = rsm009At.AddSeconds(rng.Next(1, 10)) });
    inboundCount++;

    // 3. RSM-022 activation inbound
    var rsm007At = rsm009At.AddHours(rng.Next(1, 48));
    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-022', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
        new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(800, 1500), Recv = rsm007At, Proc = rsm007At.AddSeconds(rng.Next(1, 10)) });
    inboundCount++;
}

// Processing signups: RSM outbound + RSM-001 (waiting for RSM-022)
foreach (var s in signupDefs.Where(s => s.Status == "processing"))
{
    var corrId = $"CORR-SEED-{s.Index:D4}";
    var rsmType = "RSM-001";
    var sentAt = DateTime.UtcNow.AddDays(-rng.Next(3, 10));

    await conn.ExecuteAsync(
        "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at) VALUES (@Id, @Type, @Gsrn, 'acknowledged_ok', @CorrId, @Sent, @Resp)",
        new { Id = Guid.NewGuid(), Type = rsmType, Gsrn = s.Gsrn, CorrId = corrId, Sent = sentAt, Resp = sentAt.AddMinutes(rng.Next(1, 15)) });
    outboundCount++;

    var rsm009At = sentAt.AddMinutes(rng.Next(5, 60));
    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-001', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
        new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(800, 1500), Recv = rsm009At, Proc = rsm009At.AddSeconds(rng.Next(1, 10)) });
    inboundCount++;
}

// Rejected signups: RSM outbound + RSM-001 rejection
foreach (var s in signupDefs.Where(s => s.Status == "rejected"))
{
    var corrId = $"CORR-SEED-{s.Index:D4}";
    var sentAt = DateTime.UtcNow.AddDays(-rng.Next(10, 30));

    await conn.ExecuteAsync(
        "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at, error_details) VALUES (@Id, 'RSM-001', @Gsrn, 'acknowledged_error', @CorrId, @Sent, @Resp, @Error)",
        new { Id = Guid.NewGuid(), Gsrn = s.Gsrn, CorrId = corrId, Sent = sentAt, Resp = sentAt.AddMinutes(rng.Next(1, 10)), Error = "E86 - Invalid effective date or existing active supplier" });
    outboundCount++;

    var rsm009At = sentAt.AddMinutes(rng.Next(5, 60));
    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-001', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
        new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(800, 1500), Recv = rsm009At, Proc = rsm009At.AddSeconds(rng.Next(1, 10)) });
    inboundCount++;
}

// Cancelled signups: RSM outbound + RSM-024/044 cancel outbound
foreach (var s in signupDefs.Where(s => s.Status == "cancelled"))
{
    var corrId = $"CORR-SEED-{s.Index:D4}";
    var sentAt = DateTime.UtcNow.AddDays(-rng.Next(10, 30));

    // Original RSM-001
    await conn.ExecuteAsync(
        "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at) VALUES (@Id, 'RSM-001', @Gsrn, 'acknowledged_ok', @CorrId, @Sent, @Resp)",
        new { Id = Guid.NewGuid(), Gsrn = s.Gsrn, CorrId = corrId, Sent = sentAt, Resp = sentAt.AddMinutes(rng.Next(1, 10)) });
    outboundCount++;

    // Cancel RSM-024/044
    var cancelAt = sentAt.AddHours(rng.Next(2, 48));
    var cancelType = rng.Next(2) == 0 ? "RSM-024" : "RSM-044";
    await conn.ExecuteAsync(
        "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at) VALUES (@Id, @Type, @Gsrn, 'acknowledged_ok', @CorrId, @Sent, @Resp)",
        new { Id = Guid.NewGuid(), Type = cancelType, Gsrn = s.Gsrn, CorrId = corrId, Sent = cancelAt, Resp = cancelAt.AddMinutes(rng.Next(1, 10)) });
    outboundCount++;
}

Console.WriteLine($"  {outboundCount} outbound requests");
Console.WriteLine($"  {inboundCount} inbound messages\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 6: Operational messages (no correlation ID)
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 6: Creating operational messages...");

int opMsgCount = 0;

// ~200 RSM-012 (metering data)
for (int i = 0; i < 200; i++)
{
    var receivedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 90)).AddHours(2).AddMinutes(rng.Next(0, 60));
    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-012', 'cim-metering', 'processed', @Size, @Recv, @Proc)",
        new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", Size = rng.Next(2000, 8000), Recv = receivedAt, Proc = receivedAt.AddSeconds(rng.Next(2, 30)) });
    opMsgCount++;
}

// ~40 RSM-014 (aggregation)
for (int i = 0; i < 40; i++)
{
    var receivedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 60)).AddHours(3).AddMinutes(rng.Next(0, 60));
    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-014', 'cim-aggregation', 'processed', @Size, @Recv, @Proc)",
        new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", Size = rng.Next(1500, 5000), Recv = receivedAt, Proc = receivedAt.AddSeconds(rng.Next(2, 20)) });
    opMsgCount++;
}

// ~20 RSM-004 (grid area changes)
for (int i = 0; i < 20; i++)
{
    var receivedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 45)).AddHours(rng.Next(0, 24));
    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-004', 'cim-grid', 'processed', @Size, @Recv, @Proc)",
        new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", Size = rng.Next(500, 2000), Recv = receivedAt, Proc = receivedAt.AddSeconds(rng.Next(1, 15)) });
    opMsgCount++;
}

Console.WriteLine($"  {opMsgCount} operational messages (200 RSM-012, 40 RSM-014, 20 RSM-004)\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 7: Dead letters
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 7: Creating dead letters...");

var deadLetterErrors = new[]
{
    "Invalid XML structure: missing required element 'mRID'",
    "Schema validation failed: unexpected element 'MarketDocument'",
    "Duplicate message ID detected: already processed",
    "Business rule E47: metering point not found in portfolio",
    "Business rule E86: invalid effective date for supplier switch",
    "Deserialization error: invalid date format in 'startDate'",
    "Missing mandatory field: correlationId",
    "Timeout processing message: external service unavailable",
    "Invalid GSRN format: must be 18 digits",
    "Business rule E92: conflicting active process exists",
    "Payload size exceeds maximum: 50KB limit",
    "Unknown message type: RSM-099",
    "Certificate validation failed: expired signing certificate",
    "Business rule E17: metering point type mismatch",
    "Malformed CIM XML: unclosed tag at position 1247",
};

int deadLetterCount = 0;
for (int i = 0; i < 15; i++)
{
    var receivedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 45)).AddHours(rng.Next(0, 24));
    var msgId = Guid.NewGuid();
    var messageType = new[] { "RSM-022", "RSM-001", "RSM-012", "RSM-004" }[rng.Next(4)];

    await conn.ExecuteAsync(
        "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, queue_name, status, raw_payload_size, received_at) VALUES (@Id, @DhId, @Type, 'cim-001', 'dead_lettered', @Size, @Recv)",
        new { Id = msgId, DhId = $"DH-{Guid.NewGuid():N}", Type = messageType, Size = rng.Next(500, 3000), Recv = receivedAt });
    inboundCount++;

    var resolved = i < 3; // first 3 are resolved
    await conn.ExecuteAsync(
        "INSERT INTO datahub.dead_letter (id, original_message_id, queue_name, error_reason, raw_payload, failed_at, resolved, resolved_at, resolved_by) VALUES (@Id, @OrigId, 'cim-001', @Error, @Payload::jsonb, @Failed, @Resolved, @ResolvedAt, @ResolvedBy)",
        new
        {
            Id = Guid.NewGuid(),
            OrigId = msgId.ToString(),
            Error = deadLetterErrors[i % deadLetterErrors.Length],
            Payload = "{\"raw\":\"<InvalidCIMMessage><mRID>test</mRID></InvalidCIMMessage>\"}",
            Failed = receivedAt.AddSeconds(5),
            Resolved = resolved,
            ResolvedAt = resolved ? receivedAt.AddHours(rng.Next(1, 24)) : (DateTime?)null,
            ResolvedBy = resolved ? "admin@settlement.dk" : null
        });
    deadLetterCount++;
}
Console.WriteLine($"  {deadLetterCount} dead letters (3 resolved, 12 unresolved)\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 8: Billing (periods, runs, lines)
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 8: Creating billing data...");

// 12 monthly billing periods (Jan-Dec 2025)
var billingPeriods = new List<(Guid Id, DateTime Start, DateTime End)>();
for (int month = 1; month <= 12; month++)
{
    var id = Guid.NewGuid();
    var start = new DateTime(2025, month, 1);
    var end = start.AddMonths(1).AddDays(-1);
    billingPeriods.Add((id, start, end));

    await conn.ExecuteAsync(
        "INSERT INTO settlement.billing_period (id, period_start, period_end, frequency, created_at) VALUES (@Id, @Start, @End, 'monthly', @Created)",
        new { Id = id, Start = start, End = end, Created = end.AddDays(1) });
}
Console.WriteLine($"  {billingPeriods.Count} billing periods");

// Settlement runs
var settlementRuns = new List<(Guid Id, Guid BpId, int Version, string Status)>();

// 10 completed runs (Jan-Oct v1)
for (int i = 0; i < 10; i++)
{
    var runId = Guid.NewGuid();
    var bp = billingPeriods[i];
    var executedAt = bp.End.AddDays(1).AddHours(2);
    var completedAt = executedAt.AddMinutes(rng.Next(3, 15));

    settlementRuns.Add((runId, bp.Id, 1, "completed"));
    await conn.ExecuteAsync(
        "INSERT INTO settlement.settlement_run (id, billing_period_id, version, status, executed_at, completed_at, metering_points_count) VALUES (@Id, @BpId, 1, 'completed', @Exec, @Comp, @MpCount)",
        new { Id = runId, BpId = bp.Id, Exec = executedAt, Comp = completedAt, MpCount = activeMeteringPoints.Count });
}

// 2 correction runs (v2 for Feb and May)
foreach (var monthIdx in new[] { 1, 4 }) // Feb=1, May=4
{
    var runId = Guid.NewGuid();
    var bp = billingPeriods[monthIdx];
    var executedAt = bp.End.AddDays(35).AddHours(2); // re-run ~1 month later
    var completedAt = executedAt.AddMinutes(rng.Next(3, 15));

    settlementRuns.Add((runId, bp.Id, 2, "completed"));
    await conn.ExecuteAsync(
        "INSERT INTO settlement.settlement_run (id, billing_period_id, version, status, executed_at, completed_at, metering_points_count) VALUES (@Id, @BpId, 2, 'completed', @Exec, @Comp, @MpCount)",
        new { Id = runId, BpId = bp.Id, Exec = executedAt, Comp = completedAt, MpCount = activeMeteringPoints.Count });
}

// 1 running run (Nov)
{
    var runId = Guid.NewGuid();
    settlementRuns.Add((runId, billingPeriods[10].Id, 1, "running"));
    await conn.ExecuteAsync(
        "INSERT INTO settlement.settlement_run (id, billing_period_id, version, status, executed_at, metering_points_count) VALUES (@Id, @BpId, 1, 'running', @Exec, @MpCount)",
        new { Id = runId, BpId = billingPeriods[10].Id, Exec = DateTime.UtcNow.AddMinutes(-10), MpCount = activeMeteringPoints.Count });
}

// 1 failed run (Dec)
{
    var runId = Guid.NewGuid();
    settlementRuns.Add((runId, billingPeriods[11].Id, 1, "failed"));
    await conn.ExecuteAsync(
        "INSERT INTO settlement.settlement_run (id, billing_period_id, version, status, executed_at, error_details, metering_points_count) VALUES (@Id, @BpId, 1, 'failed', @Exec, @Error, @MpCount)",
        new { Id = runId, BpId = billingPeriods[11].Id, Exec = DateTime.UtcNow.AddHours(-2), Error = "Spot price data missing for 2025-12-28 to 2025-12-31 in DK1", MpCount = 0 });
}

Console.WriteLine($"  {settlementRuns.Count} settlement runs (10 completed + 2 corrections + 1 running + 1 failed)");

// Settlement lines — only for completed runs
var chargeTypes = new[] { "energy", "grid_tariff", "system_tariff", "transmission_tariff", "electricity_tax" };
int lineCount = 0;
int batchSize = 100;
var lineBatch = new List<object>();

Console.Write("  Creating settlement lines");

foreach (var run in settlementRuns.Where(r => r.Status == "completed"))
{
    foreach (var gsrn in activeMeteringPoints)
    {
        var totalKwh = rng.Next(250, 550);
        foreach (var chargeType in chargeTypes)
        {
            var amount = chargeType switch
            {
                "energy" => totalKwh * (0.80m + (decimal)(rng.NextDouble() * 0.30)),
                "grid_tariff" => totalKwh * 0.28m,
                "system_tariff" => totalKwh * 0.054m,
                "transmission_tariff" => totalKwh * 0.049m,
                "electricity_tax" => totalKwh * 0.008m,
                _ => 0m
            };
            var vat = amount * 0.25m;

            lineBatch.Add(new { Id = Guid.NewGuid(), RunId = run.Id, MpId = gsrn, Type = chargeType, Kwh = (decimal)totalKwh, Amount = Math.Round(amount, 2), Vat = Math.Round(vat, 2), Currency = "DKK" });
            lineCount++;

            if (lineBatch.Count >= batchSize)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO settlement.settlement_line (id, settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency) VALUES (@Id, @RunId, @MpId, @Type, @Kwh, @Amount, @Vat, @Currency)",
                    lineBatch);
                lineBatch.Clear();
                if (lineCount % 5000 == 0) Console.Write(".");
            }
        }
    }
}

// Flush remaining
if (lineBatch.Count > 0)
{
    await conn.ExecuteAsync(
        "INSERT INTO settlement.settlement_line (id, settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency) VALUES (@Id, @RunId, @MpId, @Type, @Kwh, @Amount, @Vat, @Currency)",
        lineBatch);
}

Console.WriteLine();
Console.WriteLine($"  {lineCount} settlement lines\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 9: Aconto payments
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 9: Creating aconto payments...");

int acontoCount = 0;
var acontoMps = activeMeteringPoints.Take(100).ToList();
foreach (var gsrn in acontoMps)
{
    var periodStart = new DateTime(2025, rng.Next(1, 11), 1);
    var periodEnd = periodStart.AddMonths(1).AddDays(-1);
    var amount = 400m + rng.Next(0, 400);

    await conn.ExecuteAsync(
        "INSERT INTO billing.aconto_payment (id, gsrn, period_start, period_end, amount, paid_at) VALUES (@Id, @Gsrn, @PeriodStart, @PeriodEnd, @Amount, @PaidAt)",
        new { Id = Guid.NewGuid(), Gsrn = gsrn, PeriodStart = periodStart, PeriodEnd = periodEnd, Amount = amount, PaidAt = periodStart.AddDays(15) });
    acontoCount++;
}
Console.WriteLine($"  {acontoCount} aconto payments\n");

// ══════════════════════════════════════════════════════════════════════
// Phase 10: Processed message IDs (idempotency table)
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Phase 10: Populating processed message IDs...");

var processedCount = await conn.ExecuteAsync(
    "INSERT INTO datahub.processed_message_id (message_id, processed_at) SELECT datahub_message_id, processed_at FROM datahub.inbound_message WHERE status = 'processed' AND processed_at IS NOT NULL ON CONFLICT DO NOTHING");
Console.WriteLine($"  {processedCount} processed message IDs\n");

// ══════════════════════════════════════════════════════════════════════
// Summary
// ══════════════════════════════════════════════════════════════════════
Console.WriteLine("Database seeding completed!");
Console.WriteLine();
Console.WriteLine("Summary:");
Console.WriteLine($"   Grid areas:        {gridAreas.Length}");
Console.WriteLine($"   Products:          {products.Length}");
Console.WriteLine($"   Customers:         {customerMap.Count}");
Console.WriteLine($"   Signups:           {signupDefs.Count} (200 active, 10 processing, 8 registered, 7 rejected, 5 cancelled)");
Console.WriteLine($"   Metering points:   {mpCount}");
Console.WriteLine($"   Contracts:         {activeMeteringPoints.Count}");
Console.WriteLine($"   Supply periods:    {activeMeteringPoints.Count}");
Console.WriteLine($"   Process requests:  {processRequestIds.Count}");
Console.WriteLine($"   Outbound requests: {outboundCount}");
Console.WriteLine($"   Inbound messages:  {inboundCount + opMsgCount + deadLetterCount}");
Console.WriteLine($"   Dead letters:      {deadLetterCount}");
Console.WriteLine($"   Billing periods:   {billingPeriods.Count}");
Console.WriteLine($"   Settlement runs:   {settlementRuns.Count}");
Console.WriteLine($"   Settlement lines:  {lineCount}");
Console.WriteLine($"   Aconto payments:   {acontoCount}");
Console.WriteLine();
Console.WriteLine("Go check out the backoffice UI:");
Console.WriteLine("   http://localhost:5173/signups");
Console.WriteLine("   http://localhost:5173/billing");
Console.WriteLine("   http://localhost:5173/messages");
