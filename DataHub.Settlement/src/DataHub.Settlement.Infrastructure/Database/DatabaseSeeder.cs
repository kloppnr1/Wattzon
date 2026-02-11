using Dapper;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Database;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // ── Cleanup ──────────────────────────────────────────────────────
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
        await conn.ExecuteAsync("DELETE FROM portfolio.payer");
        await conn.ExecuteAsync("DELETE FROM portfolio.customer");

        var rng = new Random(42);

        // ── Phase 1: Reference data ──────────────────────────────────────
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

        // ── Phase 2: Build signup definitions ────────────────────────────
        var firstNames = new[] { "Anders", "Mette", "Lars", "Sofie", "Jens", "Camilla", "Mikkel", "Louise", "Henrik", "Anne",
            "Peter", "Maria", "Rasmus", "Ida", "Christian", "Emma", "Thomas", "Julie", "Martin", "Katrine",
            "Nikolaj", "Sara", "Kasper", "Cecilie", "Jonas", "Nanna", "Simon", "Laura", "Frederik", "Astrid" };
        var lastNames = new[] { "Hansen", "Jensen", "Nielsen", "Andersen", "Pedersen", "Christensen", "Larsen", "Sorensen",
            "Rasmussen", "Petersen", "Madsen", "Kristensen", "Olsen", "Thomsen", "Poulsen", "Johansen",
            "Knudsen", "Mortensen", "Moller", "Jakobsen" };
        var companyNames = new[] { "ApS", "A/S", "I/S", "K/S" };
        var companyPrefixes = new[] { "Nordic", "Dansk", "Green", "Sol", "Vind", "El", "Smart", "Digital", "Eco", "Nord" };
        var companySuffixes = new[] { "Energy", "Tech", "Solutions", "Service", "Group", "Power", "Systems", "Trading", "Design", "Consult" };

        // Danish address data for billing addresses
        var streets = new[] { "Vesterbrogade", "Nørrebrogade", "Østerbrogade", "Amagerbrogade", "Gammel Kongevej",
            "Strandvejen", "Kongens Nytorv", "Gothersgade", "Bredgade", "Store Kongensgade",
            "Frederiksberg Allé", "Jagtvej", "Tagensvej", "Vigerslev Allé", "Roskildevej",
            "Hovedgaden", "Jernbanegade", "Algade", "Storegade", "Torvet" };
        var cities = new[] {
            ("1620", "København V"), ("2200", "København N"), ("2100", "København Ø"), ("2300", "København S"),
            ("1850", "Frederiksberg C"), ("2900", "Hellerup"), ("8000", "Aarhus C"), ("5000", "Odense C"),
            ("9000", "Aalborg"), ("7100", "Vejle"), ("6000", "Kolding"), ("4000", "Roskilde"),
            ("2800", "Kongens Lyngby"), ("2630", "Taastrup"), ("2750", "Ballerup") };
        var floors = new[] { (string?)null, "st", "1", "2", "3", "4", "5" };
        var doors = new[] { (string?)null, "th", "tv", "mf", "1", "2", "3" };

        string PickGridArea(int index) => (index % 100) switch
        {
            < 48 => "543",
            < 80 => "344",
            _ => "740"
        };

        string GridGln(string code) => code switch { "543" => "5790000432752", "344" => "5790001089030", _ => "5790000705689" };
        string GridPriceArea(string code) => code == "543" ? "DK2" : "DK1";
        string MakeGsrn(int n) => $"57131318040{n:D7}";

        var signupDefs = new List<(string Name, string CprCvr, string ContactType, string SignupContactType, string Gsrn, string Status, string Type, string GridArea, int Index)>();
        int gsrnCounter = 1;

        // 200 active signups
        for (int i = 0; i < 200; i++)
        {
            bool isBusiness = i % 6 == 0;
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
            signupDefs.Add((name, cprCvr, contactType, signupContactType, MakeGsrn(gsrnCounter++), "active", i % 5 == 0 ? "move_in" : "switch", PickGridArea(i), i));
        }

        // 10 processing
        for (int i = 0; i < 10; i++)
            signupDefs.Add(($"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}", $"{2000000000L + i:D10}", "private", "person", MakeGsrn(gsrnCounter++), "processing", "switch", PickGridArea(200 + i), 200 + i));

        // 8 registered
        for (int i = 0; i < 8; i++)
            signupDefs.Add(($"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}", $"{3000000000L + i:D10}", "private", "person", MakeGsrn(gsrnCounter++), "registered", "switch", PickGridArea(210 + i), 210 + i));

        // 7 rejected
        for (int i = 0; i < 7; i++)
            signupDefs.Add(($"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}", $"{4000000000L + i:D10}", "private", "person", MakeGsrn(gsrnCounter++), "rejected", "switch", PickGridArea(218 + i), 218 + i));

        // 5 cancelled
        for (int i = 0; i < 5; i++)
            signupDefs.Add(($"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}", $"{5000000000L + i:D10}", "private", "person", MakeGsrn(gsrnCounter++), "cancelled", "switch", PickGridArea(225 + i), 225 + i));

        // ── Phase 2b: Create customers ───────────────────────────────────
        var customerMap = new Dictionary<string, Guid>();
        int custIdx = 0;
        foreach (var s in signupDefs.Where(s => s.Status == "active"))
        {
            if (customerMap.ContainsKey(s.CprCvr)) continue;
            var customerId = Guid.NewGuid();
            customerMap[s.CprCvr] = customerId;
            var city = cities[custIdx % cities.Length];
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.customer (id, name, cpr_cvr, contact_type, status, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city) VALUES (@Id, @Name, @CprCvr, @ContactType, 'active', @Street, @HouseNum, @Floor, @Door, @PostalCode, @City)",
                new { Id = customerId, Name = s.Name, CprCvr = s.CprCvr, ContactType = s.ContactType,
                    Street = streets[custIdx % streets.Length],
                    HouseNum = $"{1 + custIdx % 120}",
                    Floor = floors[custIdx % floors.Length],
                    Door = doors[custIdx % doors.Length],
                    PostalCode = city.Item1, City = city.Item2 });
            custIdx++;
        }

        for (int i = 0; i < 260; i++)
        {
            bool isBusiness = i % 4 == 0;
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
            var city = cities[custIdx % cities.Length];
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.customer (id, name, cpr_cvr, contact_type, status, billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city) VALUES (@Id, @Name, @CprCvr, @ContactType, 'active', @Street, @HouseNum, @Floor, @Door, @PostalCode, @City)",
                new { Id = customerId, Name = name, CprCvr = cprCvr, ContactType = contactType,
                    Street = streets[custIdx % streets.Length],
                    HouseNum = $"{1 + custIdx % 120}",
                    Floor = floors[custIdx % floors.Length],
                    Door = doors[custIdx % doors.Length],
                    PostalCode = city.Item1, City = city.Item2 });
            custIdx++;
        }

        // ── Phase 3: Signups + process requests ─────────────────────────
        var processRequestIds = new Dictionary<int, Guid>();
        var signupIds = new Dictionary<int, Guid>();
        int signupNumber = 1;

        foreach (var s in signupDefs)
        {
            var signupId = Guid.NewGuid();
            signupIds[s.Index] = signupId;
            var effectiveDate = s.Status == "active" ? new DateTime(2025, 1, 1) : DateTime.UtcNow.Date.AddDays(30);
            var customerId = s.Status == "active" && customerMap.TryGetValue(s.CprCvr, out var cid) ? cid : (Guid?)null;

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
                        Id = processRequestId.Value, ProcessType = processType, Gsrn = s.Gsrn, Status = processStatus,
                        EffectiveDate = effectiveDate, CorrId = corrId,
                        Requested = DateTime.UtcNow.AddDays(-rng.Next(10, 60)),
                        Completed = processStatus == "completed" ? DateTime.UtcNow.AddDays(-rng.Next(1, 10)) : (DateTime?)null,
                        Created = DateTime.UtcNow.AddDays(-rng.Next(10, 60))
                    });
            }

            var signupCity = cities[signupNumber % cities.Length];
            await conn.ExecuteAsync(
                @"INSERT INTO portfolio.signup (id, signup_number, dar_id, gsrn, customer_id, product_id, process_request_id,
                    type, effective_date, status, created_at, customer_name, customer_cpr_cvr, customer_contact_type,
                    billing_street, billing_house_number, billing_floor, billing_door, billing_postal_code, billing_city)
                  VALUES (@Id, @SignupNum, @DarId, @Gsrn, @CustomerId, @ProductId, @ProcessRequestId,
                    @Type, @EffectiveDate, @Status, @Created, @CustomerName, @CustomerCprCvr, @CustomerContactType,
                    @BillStreet, @BillHouseNum, @BillFloor, @BillDoor, @BillPostalCode, @BillCity)",
                new
                {
                    Id = signupId, SignupNum = $"SGN-2025-{signupNumber:D5}",
                    DarId = $"0a3f5000-{signupNumber:D4}-62c3-e044-0003ba298018",
                    Gsrn = s.Gsrn, CustomerId = customerId,
                    ProductId = productIds[s.Index % productIds.Count],
                    ProcessRequestId = processRequestId,
                    Type = s.Type, EffectiveDate = effectiveDate, Status = s.Status,
                    Created = DateTime.UtcNow.AddDays(-rng.Next(10, 60)),
                    CustomerName = s.Name, CustomerCprCvr = s.CprCvr, CustomerContactType = s.SignupContactType,
                    BillStreet = streets[signupNumber % streets.Length],
                    BillHouseNum = $"{1 + signupNumber % 120}",
                    BillFloor = floors[signupNumber % floors.Length],
                    BillDoor = doors[signupNumber % doors.Length],
                    BillPostalCode = signupCity.Item1, BillCity = signupCity.Item2
                });
            signupNumber++;
        }

        // ── Phase 3b: Additional processes with operationally interesting statuses ──
        var extraProcessTypes = new[] { "supplier_switch", "move_in", "end_of_supply" };
        var extraProcesses = new List<(Guid Id, string Status, string ProcessType, string Gsrn, DateTime Created)>();

        // 6 pending (just created, not yet sent)
        for (int i = 0; i < 6; i++)
        {
            var pid = Guid.NewGuid();
            var gsrn = MakeGsrn(gsrnCounter++);
            var created = DateTime.UtcNow.AddHours(-rng.Next(1, 12));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_request (id, process_type, gsrn, status, effective_date, datahub_correlation_id, requested_at, created_at) VALUES (@Id, @ProcessType, @Gsrn, 'pending', @EffDate, @CorrId, @Created, @Created)",
                new { Id = pid, ProcessType = extraProcessTypes[i % extraProcessTypes.Length], Gsrn = gsrn, EffDate = DateTime.UtcNow.Date.AddDays(14 + i), CorrId = $"CORR-PEND-{i:D4}", Created = created });
            extraProcesses.Add((pid, "pending", extraProcessTypes[i % extraProcessTypes.Length], gsrn, created));
        }

        // 8 sent_to_datahub (awaiting acknowledgement — the most operationally relevant)
        for (int i = 0; i < 8; i++)
        {
            var pid = Guid.NewGuid();
            var gsrn = MakeGsrn(gsrnCounter++);
            var created = DateTime.UtcNow.AddDays(-rng.Next(1, 5)).AddHours(-rng.Next(1, 12));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_request (id, process_type, gsrn, status, effective_date, datahub_correlation_id, requested_at, created_at) VALUES (@Id, @ProcessType, @Gsrn, 'sent_to_datahub', @EffDate, @CorrId, @Created, @Created)",
                new { Id = pid, ProcessType = extraProcessTypes[i % extraProcessTypes.Length], Gsrn = gsrn, EffDate = DateTime.UtcNow.Date.AddDays(10 + i), CorrId = $"CORR-SENT-{i:D4}", Created = created });
            extraProcesses.Add((pid, "sent_to_datahub", extraProcessTypes[i % extraProcessTypes.Length], gsrn, created));
        }

        // 5 acknowledged (waiting for effectuation date)
        for (int i = 0; i < 5; i++)
        {
            var pid = Guid.NewGuid();
            var gsrn = MakeGsrn(gsrnCounter++);
            var created = DateTime.UtcNow.AddDays(-rng.Next(5, 15));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_request (id, process_type, gsrn, status, effective_date, datahub_correlation_id, requested_at, created_at) VALUES (@Id, @ProcessType, @Gsrn, 'acknowledged', @EffDate, @CorrId, @Created, @Created)",
                new { Id = pid, ProcessType = extraProcessTypes[i % extraProcessTypes.Length], Gsrn = gsrn, EffDate = DateTime.UtcNow.Date.AddDays(5 + i * 3), CorrId = $"CORR-ACK-{i:D4}", Created = created });
            extraProcesses.Add((pid, "acknowledged", extraProcessTypes[i % extraProcessTypes.Length], gsrn, created));
        }

        // 3 cancellation_pending (cancellation request sent, awaiting DataHub response)
        for (int i = 0; i < 3; i++)
        {
            var pid = Guid.NewGuid();
            var gsrn = MakeGsrn(gsrnCounter++);
            var created = DateTime.UtcNow.AddDays(-rng.Next(10, 20));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_request (id, process_type, gsrn, status, effective_date, datahub_correlation_id, requested_at, created_at) VALUES (@Id, @ProcessType, @Gsrn, 'cancellation_pending', @EffDate, @CorrId, @Created, @Created)",
                new { Id = pid, ProcessType = "supplier_switch", Gsrn = gsrn, EffDate = DateTime.UtcNow.Date.AddDays(7 + i), CorrId = $"CORR-CANP-{i:D4}", Created = created });
            extraProcesses.Add((pid, "cancellation_pending", "supplier_switch", gsrn, created));
        }

        // ── Phase 3c: Process events for all process requests ──────────
        // Events for existing signup-linked processes
        foreach (var s in signupDefs.Where(s => s.Status != "registered"))
        {
            var prId = processRequestIds[s.Index];
            var processStatus = s.Status switch { "active" => "completed", "processing" => "effectuation_pending", "rejected" => "rejected", "cancelled" => "cancelled", _ => "pending" };
            var baseTime = DateTime.UtcNow.AddDays(-rng.Next(20, 55));

            // All processes start with 'created'
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'created', 'system')",
                new { Id = Guid.NewGuid(), PrId = prId, At = baseTime });

            if (processStatus == "pending") continue;

            // sent
            var sentAt = baseTime.AddMinutes(rng.Next(5, 120));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'sent', 'system')",
                new { Id = Guid.NewGuid(), PrId = prId, At = sentAt });

            if (processStatus == "rejected")
            {
                var rejAt = sentAt.AddMinutes(rng.Next(5, 60));
                await conn.ExecuteAsync(
                    "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, payload, source) VALUES (@Id, @PrId, @At, 'rejection_reason', @Payload::jsonb, 'datahub')",
                    new { Id = Guid.NewGuid(), PrId = prId, At = rejAt, Payload = "{\"reason\": \"E86 - Invalid effective date or existing active supplier\"}" });
                continue;
            }

            // acknowledged
            var ackAt = sentAt.AddMinutes(rng.Next(2, 30));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'acknowledged', 'datahub')",
                new { Id = Guid.NewGuid(), PrId = prId, At = ackAt });

            if (processStatus == "cancelled")
            {
                var cancelSentAt = ackAt.AddHours(rng.Next(2, 48));
                await conn.ExecuteAsync(
                    "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'cancellation_sent', 'system')",
                    new { Id = Guid.NewGuid(), PrId = prId, At = cancelSentAt });
                var cancelledAt = cancelSentAt.AddMinutes(rng.Next(5, 60));
                await conn.ExecuteAsync(
                    "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'cancelled', 'datahub')",
                    new { Id = Guid.NewGuid(), PrId = prId, At = cancelledAt });
                continue;
            }

            // awaiting_effectuation
            var awaitAt = ackAt.AddHours(rng.Next(1, 24));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'awaiting_effectuation', 'datahub')",
                new { Id = Guid.NewGuid(), PrId = prId, At = awaitAt });

            if (processStatus == "effectuation_pending") continue;

            // completed
            var completedAt = awaitAt.AddDays(rng.Next(1, 10));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'completed', 'datahub')",
                new { Id = Guid.NewGuid(), PrId = prId, At = completedAt });
        }

        // Events for extra processes (new statuses)
        foreach (var (pid, status, _, _, created) in extraProcesses)
        {
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'created', 'system')",
                new { Id = Guid.NewGuid(), PrId = pid, At = created });

            if (status == "pending") continue;

            var sentAt = created.AddMinutes(rng.Next(5, 60));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'sent', 'system')",
                new { Id = Guid.NewGuid(), PrId = pid, At = sentAt });

            if (status == "sent_to_datahub") continue;

            var ackAt = sentAt.AddMinutes(rng.Next(2, 15));
            await conn.ExecuteAsync(
                "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'acknowledged', 'datahub')",
                new { Id = Guid.NewGuid(), PrId = pid, At = ackAt });

            if (status == "acknowledged") continue;

            if (status == "cancellation_pending")
            {
                var awaitAt = ackAt.AddHours(rng.Next(1, 12));
                await conn.ExecuteAsync(
                    "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'awaiting_effectuation', 'datahub')",
                    new { Id = Guid.NewGuid(), PrId = pid, At = awaitAt });
                var cancelSentAt = awaitAt.AddHours(rng.Next(1, 24));
                await conn.ExecuteAsync(
                    "INSERT INTO lifecycle.process_event (id, process_request_id, occurred_at, event_type, source) VALUES (@Id, @PrId, @At, 'cancellation_sent', 'system')",
                    new { Id = Guid.NewGuid(), PrId = pid, At = cancelSentAt });
            }
        }

        // ── Phase 4: Portfolio ───────────────────────────────────────────
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
        }

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
        }

        // 10 multi-MP customers
        foreach (var s in signupDefs.Where(s => s.Status == "active").Take(10))
        {
            var gsrn2 = MakeGsrn(gsrnCounter++);
            var ga = s.GridArea;
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area) VALUES (@Gsrn, 'E17', 'flex', 'connected', @GridArea, @Gln, @PriceArea)",
                new { Gsrn = gsrn2, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga) });
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.contract (id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date) VALUES (@Id, @CustomerId, @Gsrn, @ProductId, 'monthly', 'aconto', @Start)",
                new { Id = Guid.NewGuid(), CustomerId = customerMap[s.CprCvr], Gsrn = gsrn2, ProductId = productIds[s.Index % productIds.Count], Start = new DateTime(2025, 1, 1) });
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.supply_period (id, gsrn, start_date) VALUES (@Id, @Gsrn, @Start)",
                new { Id = Guid.NewGuid(), Gsrn = gsrn2, Start = new DateTime(2025, 1, 1) });
            activeMeteringPoints.Add(gsrn2);
        }

        // ── Phase 4b: Payers ───────────────────────────────────────────
        // ~10% of active customers have a third-party payer (e.g. employer, parent, housing association)
        var payerNames = new[] { "Boligforeningen Sjælland", "Dansk Industri ApS", "Nordisk Ejendomme A/S",
            "Hansen & Søn Holding", "Grøn Bolig K/S", "Energi Danmark A/S", "Vestjysk Boligselskab",
            "København Kommune", "Aarhus Boligforening", "Norden Facility I/S" };
        var payerMap = new Dictionary<int, Guid>();
        var activeSignups = signupDefs.Where(s => s.Status == "active").ToList();

        for (int i = 0; i < 10; i++)
        {
            var payerId = Guid.NewGuid();
            payerMap[i] = payerId;
            var payerCity = cities[i % cities.Length];
            await conn.ExecuteAsync(
                @"INSERT INTO portfolio.payer (id, name, cpr_cvr, contact_type, email, phone,
                    billing_street, billing_house_number, billing_postal_code, billing_city)
                  VALUES (@Id, @Name, @CprCvr, 'business', @Email, @Phone,
                    @Street, @HouseNum, @PostalCode, @City)",
                new {
                    Id = payerId, Name = payerNames[i],
                    CprCvr = $"{70000000 + i:D8}",
                    Email = $"faktura@{payerNames[i].ToLower().Replace(" ", "").Replace("&", "").Replace("/", "")[..8]}.dk",
                    Phone = $"+45 {70000000 + rng.Next(100000, 999999)}",
                    Street = streets[(i + 5) % streets.Length],
                    HouseNum = $"{10 + i * 3}",
                    PostalCode = payerCity.Item1, City = payerCity.Item2 });
        }

        // Link ~20 contracts to payers (every 10th active signup)
        for (int i = 0; i < activeSignups.Count; i += 10)
        {
            var s = activeSignups[i];
            var payerIdx = (i / 10) % payerMap.Count;
            await conn.ExecuteAsync(
                "UPDATE portfolio.contract SET payer_id = @PayerId WHERE gsrn = @Gsrn",
                new { PayerId = payerMap[payerIdx], Gsrn = s.Gsrn });
        }

        // 5 disconnected
        for (int i = 0; i < 5; i++)
        {
            var gsrn = MakeGsrn(gsrnCounter++);
            var ga = PickGridArea(i);
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area, deactivated_at) VALUES (@Gsrn, 'E17', 'flex', 'disconnected', @GridArea, @Gln, @PriceArea, @DeactivatedAt)",
                new { Gsrn = gsrn, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga), DeactivatedAt = DateTime.UtcNow.AddDays(-rng.Next(30, 180)) });
        }

        // ── Phase 5: Process-linked messages ─────────────────────────────
        foreach (var s in signupDefs.Where(s => s.Status == "active"))
        {
            var corrId = $"CORR-SEED-{s.Index:D4}";
            var rsmType = s.Type == "move_in" ? "RSM-001" : "RSM-001";
            var sentAt = DateTime.UtcNow.AddDays(-rng.Next(15, 55));

            await conn.ExecuteAsync(
                "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at) VALUES (@Id, @Type, @Gsrn, 'acknowledged_ok', @CorrId, @Sent, @Resp)",
                new { Id = Guid.NewGuid(), Type = rsmType, Gsrn = s.Gsrn, CorrId = corrId, Sent = sentAt, Resp = sentAt.AddMinutes(rng.Next(1, 15)) });

            var rsm009At = sentAt.AddMinutes(rng.Next(2, 30));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-001', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(800, 1500), Recv = rsm009At, Proc = rsm009At.AddSeconds(rng.Next(1, 10)) });

            // RSM-028: Customer data
            var rsm028At = rsm009At.AddMinutes(rng.Next(1, 15));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-028', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(600, 1200), Recv = rsm028At, Proc = rsm028At.AddSeconds(rng.Next(1, 5)) });

            // RSM-031: Price attachments
            var rsm031At = rsm028At.AddMinutes(rng.Next(1, 10));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-031', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(500, 1000), Recv = rsm031At, Proc = rsm031At.AddSeconds(rng.Next(1, 5)) });

            var rsm007At = rsm031At.AddHours(rng.Next(1, 48));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-022', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(800, 1500), Recv = rsm007At, Proc = rsm007At.AddSeconds(rng.Next(1, 10)) });
        }

        foreach (var s in signupDefs.Where(s => s.Status == "processing"))
        {
            var corrId = $"CORR-SEED-{s.Index:D4}";
            var sentAt = DateTime.UtcNow.AddDays(-rng.Next(3, 10));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at) VALUES (@Id, 'RSM-001', @Gsrn, 'acknowledged_ok', @CorrId, @Sent, @Resp)",
                new { Id = Guid.NewGuid(), Gsrn = s.Gsrn, CorrId = corrId, Sent = sentAt, Resp = sentAt.AddMinutes(rng.Next(1, 15)) });
            var rsm009At = sentAt.AddMinutes(rng.Next(5, 60));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-001', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(800, 1500), Recv = rsm009At, Proc = rsm009At.AddSeconds(rng.Next(1, 10)) });
        }

        foreach (var s in signupDefs.Where(s => s.Status == "rejected"))
        {
            var corrId = $"CORR-SEED-{s.Index:D4}";
            var sentAt = DateTime.UtcNow.AddDays(-rng.Next(10, 30));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at, error_details) VALUES (@Id, 'RSM-001', @Gsrn, 'acknowledged_error', @CorrId, @Sent, @Resp, @Error)",
                new { Id = Guid.NewGuid(), Gsrn = s.Gsrn, CorrId = corrId, Sent = sentAt, Resp = sentAt.AddMinutes(rng.Next(1, 10)), Error = "E86 - Invalid effective date or existing active supplier" });
            var rsm009At = sentAt.AddMinutes(rng.Next(5, 60));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, correlation_id, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-001', @CorrId, 'cim-001', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", CorrId = corrId, Size = rng.Next(800, 1500), Recv = rsm009At, Proc = rsm009At.AddSeconds(rng.Next(1, 10)) });
        }

        foreach (var s in signupDefs.Where(s => s.Status == "cancelled"))
        {
            var corrId = $"CORR-SEED-{s.Index:D4}";
            var sentAt = DateTime.UtcNow.AddDays(-rng.Next(10, 30));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at) VALUES (@Id, 'RSM-001', @Gsrn, 'acknowledged_ok', @CorrId, @Sent, @Resp)",
                new { Id = Guid.NewGuid(), Gsrn = s.Gsrn, CorrId = corrId, Sent = sentAt, Resp = sentAt.AddMinutes(rng.Next(1, 10)) });
            var cancelAt = sentAt.AddHours(rng.Next(2, 48));
            var cancelType = rng.Next(2) == 0 ? "RSM-024" : "RSM-044";
            await conn.ExecuteAsync(
                "INSERT INTO datahub.outbound_request (id, process_type, gsrn, status, correlation_id, sent_at, response_at) VALUES (@Id, @Type, @Gsrn, 'acknowledged_ok', @CorrId, @Sent, @Resp)",
                new { Id = Guid.NewGuid(), Type = cancelType, Gsrn = s.Gsrn, CorrId = corrId, Sent = cancelAt, Resp = cancelAt.AddMinutes(rng.Next(1, 10)) });
        }

        // ── Phase 6: Operational messages ────────────────────────────────
        for (int i = 0; i < 200; i++)
        {
            var receivedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 90)).AddHours(2).AddMinutes(rng.Next(0, 60));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-012', 'cim-metering', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", Size = rng.Next(2000, 8000), Recv = receivedAt, Proc = receivedAt.AddSeconds(rng.Next(2, 30)) });
        }

        for (int i = 0; i < 40; i++)
        {
            var receivedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 60)).AddHours(3).AddMinutes(rng.Next(0, 60));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-014', 'cim-aggregation', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", Size = rng.Next(1500, 5000), Recv = receivedAt, Proc = receivedAt.AddSeconds(rng.Next(2, 20)) });
        }

        for (int i = 0; i < 20; i++)
        {
            var receivedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 45)).AddHours(rng.Next(0, 24));
            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, queue_name, status, raw_payload_size, received_at, processed_at) VALUES (@Id, @DhId, 'RSM-004', 'cim-grid', 'processed', @Size, @Recv, @Proc)",
                new { Id = Guid.NewGuid(), DhId = $"DH-{Guid.NewGuid():N}", Size = rng.Next(500, 2000), Recv = receivedAt, Proc = receivedAt.AddSeconds(rng.Next(1, 15)) });
        }

        // ── Phase 7: Dead letters ────────────────────────────────────────
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

        for (int i = 0; i < 15; i++)
        {
            var receivedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 45)).AddHours(rng.Next(0, 24));
            var msgId = Guid.NewGuid();
            var messageType = new[] { "RSM-022", "RSM-001", "RSM-012", "RSM-004" }[rng.Next(4)];

            await conn.ExecuteAsync(
                "INSERT INTO datahub.inbound_message (id, datahub_message_id, message_type, queue_name, status, raw_payload_size, received_at) VALUES (@Id, @DhId, @Type, 'cim-001', 'dead_lettered', @Size, @Recv)",
                new { Id = msgId, DhId = $"DH-{Guid.NewGuid():N}", Type = messageType, Size = rng.Next(500, 3000), Recv = receivedAt });

            var resolved = i < 3;
            await conn.ExecuteAsync(
                "INSERT INTO datahub.dead_letter (id, original_message_id, queue_name, error_reason, raw_payload, failed_at, resolved, resolved_at, resolved_by) VALUES (@Id, @OrigId, 'cim-001', @Error, @Payload::jsonb, @Failed, @Resolved, @ResolvedAt, @ResolvedBy)",
                new
                {
                    Id = Guid.NewGuid(), OrigId = msgId.ToString(),
                    Error = deadLetterErrors[i % deadLetterErrors.Length],
                    Payload = "{\"raw\":\"<InvalidCIMMessage><mRID>test</mRID></InvalidCIMMessage>\"}",
                    Failed = receivedAt.AddSeconds(5), Resolved = resolved,
                    ResolvedAt = resolved ? receivedAt.AddHours(rng.Next(1, 24)) : (DateTime?)null,
                    ResolvedBy = resolved ? "admin@settlement.dk" : null
                });
        }

        // ── Phase 8: Billing ─────────────────────────────────────────────
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

        var settlementRuns = new List<(Guid Id, Guid BpId, int Version, string Status)>();

        for (int i = 0; i < 10; i++)
        {
            var runId = Guid.NewGuid();
            var bp = billingPeriods[i];
            var executedAt = bp.End.AddDays(1).AddHours(2);
            settlementRuns.Add((runId, bp.Id, 1, "completed"));
            await conn.ExecuteAsync(
                "INSERT INTO settlement.settlement_run (id, billing_period_id, version, status, executed_at, completed_at, metering_points_count) VALUES (@Id, @BpId, 1, 'completed', @Exec, @Comp, @MpCount)",
                new { Id = runId, BpId = bp.Id, Exec = executedAt, Comp = executedAt.AddMinutes(rng.Next(3, 15)), MpCount = activeMeteringPoints.Count });
        }

        foreach (var monthIdx in new[] { 1, 4 })
        {
            var runId = Guid.NewGuid();
            var bp = billingPeriods[monthIdx];
            var executedAt = bp.End.AddDays(35).AddHours(2);
            settlementRuns.Add((runId, bp.Id, 2, "completed"));
            await conn.ExecuteAsync(
                "INSERT INTO settlement.settlement_run (id, billing_period_id, version, status, executed_at, completed_at, metering_points_count) VALUES (@Id, @BpId, 2, 'completed', @Exec, @Comp, @MpCount)",
                new { Id = runId, BpId = bp.Id, Exec = executedAt, Comp = executedAt.AddMinutes(rng.Next(3, 15)), MpCount = activeMeteringPoints.Count });
        }

        {
            var runId = Guid.NewGuid();
            settlementRuns.Add((runId, billingPeriods[10].Id, 1, "running"));
            await conn.ExecuteAsync(
                "INSERT INTO settlement.settlement_run (id, billing_period_id, version, status, executed_at, metering_points_count) VALUES (@Id, @BpId, 1, 'running', @Exec, @MpCount)",
                new { Id = runId, BpId = billingPeriods[10].Id, Exec = DateTime.UtcNow.AddMinutes(-10), MpCount = activeMeteringPoints.Count });
        }

        {
            var runId = Guid.NewGuid();
            settlementRuns.Add((runId, billingPeriods[11].Id, 1, "failed"));
            await conn.ExecuteAsync(
                "INSERT INTO settlement.settlement_run (id, billing_period_id, version, status, executed_at, error_details, metering_points_count) VALUES (@Id, @BpId, 1, 'failed', @Exec, @Error, @MpCount)",
                new { Id = runId, BpId = billingPeriods[11].Id, Exec = DateTime.UtcNow.AddHours(-2), Error = "Spot price data missing for 2025-12-28 to 2025-12-31 in DK1", MpCount = 0 });
        }

        // Settlement lines
        var chargeTypes = new[] { "energy", "grid_tariff", "system_tariff", "transmission_tariff", "electricity_tax" };
        var lineBatch = new List<object>();

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

                    if (lineBatch.Count >= 100)
                    {
                        await conn.ExecuteAsync(
                            "INSERT INTO settlement.settlement_line (id, settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency) VALUES (@Id, @RunId, @MpId, @Type, @Kwh, @Amount, @Vat, @Currency)",
                            lineBatch);
                        lineBatch.Clear();
                    }
                }
            }
        }

        if (lineBatch.Count > 0)
        {
            await conn.ExecuteAsync(
                "INSERT INTO settlement.settlement_line (id, settlement_run_id, metering_point_id, charge_type, total_kwh, total_amount, vat_amount, currency) VALUES (@Id, @RunId, @MpId, @Type, @Kwh, @Amount, @Vat, @Currency)",
                lineBatch);
        }

        // ── Phase 9: Aconto payments ─────────────────────────────────────
        foreach (var gsrn in activeMeteringPoints.Take(100))
        {
            var periodStart = new DateTime(2025, rng.Next(1, 11), 1);
            var periodEnd = periodStart.AddMonths(1).AddDays(-1);
            await conn.ExecuteAsync(
                "INSERT INTO billing.aconto_payment (id, gsrn, period_start, period_end, amount, paid_at, currency) VALUES (@Id, @Gsrn, @PeriodStart, @PeriodEnd, @Amount, @PaidAt, @Currency)",
                new { Id = Guid.NewGuid(), Gsrn = gsrn, PeriodStart = periodStart, PeriodEnd = periodEnd, Amount = 400m + rng.Next(0, 400), PaidAt = periodStart.AddDays(15), Currency = "DKK" });
        }

        // ── Phase 10: Metering data history (for correction testing) ──────
        // Insert revised metering data for the first 5 active metering points
        // across October 2025, so corrections can be triggered via the UI.
        var correctionTestGsrns = activeMeteringPoints.Take(5).ToList();
        foreach (var gsrn in correctionTestGsrns)
        {
            for (int day = 1; day <= 31; day++)
            {
                for (int hour = 0; hour < 24; hour++)
                {
                    // Simulate a metering revision: original value vs corrected value
                    var ts = new DateTime(2025, 10, day, hour, 0, 0, DateTimeKind.Utc);
                    if (day > 28 && ts.Month != 10) break; // skip invalid dates
                    var previousKwh = 0.3m + (decimal)(rng.NextDouble() * 0.5);
                    var newKwh = previousKwh + 0.05m + (decimal)(rng.NextDouble() * 0.15); // always a positive delta
                    await conn.ExecuteAsync(
                        "INSERT INTO metering.metering_data_history (metering_point_id, timestamp, previous_kwh, new_kwh, previous_message_id, new_message_id) VALUES (@Gsrn, @Ts, @Prev, @New, @PrevMsg, @NewMsg)",
                        new { Gsrn = gsrn, Ts = ts, Prev = Math.Round(previousKwh, 6), New = Math.Round(newKwh, 6), PrevMsg = $"MSG-ORIG-{day:D2}{hour:D2}", NewMsg = $"MSG-REV-{day:D2}{hour:D2}" });
                }
            }
        }

        // Insert a few pre-existing correction records so the list page has data
        await conn.ExecuteAsync("DELETE FROM settlement.correction_settlement");
        var chargeTypesForCorr = new[] { "energy", "grid_tariff", "system_tariff", "transmission_tariff", "electricity_tax" };
        for (int c = 0; c < 3; c++)
        {
            var batchId = Guid.NewGuid();
            var corrGsrn = correctionTestGsrns[c];
            var origRunId = settlementRuns.FirstOrDefault(r => r.Status == "completed").Id;
            var corrAmounts = new[] { 12.45m, 3.20m, 0.85m, 0.72m, 0.10m };
            var corrKwh = 15.5m + c * 3.2m;

            for (int ct = 0; ct < chargeTypesForCorr.Length; ct++)
            {
                var subtotal = corrAmounts.Sum();
                var vatAmt = Math.Round(subtotal * 0.25m, 2);
                var totalAmt = subtotal + vatAmt;
                await conn.ExecuteAsync(
                    @"INSERT INTO settlement.correction_settlement
                        (correction_batch_id, metering_point_id, period_start, period_end, original_run_id,
                         delta_kwh, charge_type, delta_amount, trigger_type, status, vat_amount, total_amount, note)
                      VALUES
                        (@BatchId, @Gsrn, @PeriodStart, @PeriodEnd, @OrigRunId,
                         @DeltaKwh, @ChargeType, @DeltaAmount, @TriggerType, 'completed', @VatAmount, @TotalAmount, @Note)",
                    new
                    {
                        BatchId = batchId, Gsrn = corrGsrn,
                        PeriodStart = new DateTime(2025, 10, 1), PeriodEnd = new DateTime(2025, 10, 31),
                        OrigRunId = origRunId, DeltaKwh = corrKwh, ChargeType = chargeTypesForCorr[ct],
                        DeltaAmount = corrAmounts[ct], TriggerType = c == 0 ? "auto" : "manual",
                        VatAmount = vatAmt, TotalAmount = totalAmt,
                        Note = c == 0 ? (string?)null : $"Manual correction test #{c}"
                    });
            }
        }

        // ── Phase 11: Processed message IDs ──────────────────────────────
        await conn.ExecuteAsync(
            "INSERT INTO datahub.processed_message_id (message_id, processed_at) SELECT datahub_message_id, processed_at FROM datahub.inbound_message WHERE status = 'processed' AND processed_at IS NOT NULL ON CONFLICT DO NOTHING");
    }
}
