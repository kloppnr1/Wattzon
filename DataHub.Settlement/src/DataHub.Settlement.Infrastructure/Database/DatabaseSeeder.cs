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
        await conn.ExecuteAsync("DELETE FROM billing.payment_allocation");
        await conn.ExecuteAsync("DELETE FROM billing.invoice_line");
        await conn.ExecuteAsync("DELETE FROM billing.payment");
        await conn.ExecuteAsync("DELETE FROM billing.invoice");
        await conn.ExecuteAsync("DELETE FROM settlement.correction_settlement");
        await conn.ExecuteAsync("DELETE FROM settlement.settlement_line");
        await conn.ExecuteAsync("DELETE FROM settlement.settlement_run");
        await conn.ExecuteAsync("DELETE FROM settlement.billing_period");
        await conn.ExecuteAsync("DELETE FROM billing.aconto_payment");
        await conn.ExecuteAsync("DELETE FROM portfolio.supply_period");
        await conn.ExecuteAsync("DELETE FROM portfolio.contract");
        await conn.ExecuteAsync("DELETE FROM portfolio.metering_point");
        await conn.ExecuteAsync("DELETE FROM portfolio.signup");
        await conn.ExecuteAsync("DELETE FROM lifecycle.process_event");
        await conn.ExecuteAsync("DELETE FROM lifecycle.process_request");
        await conn.ExecuteAsync("DELETE FROM datahub.dead_letter");
        await conn.ExecuteAsync("DELETE FROM datahub.processed_message_id");
        await conn.ExecuteAsync("DELETE FROM datahub.outbound_request");
        await conn.ExecuteAsync("DELETE FROM datahub.inbound_message");
        await conn.ExecuteAsync("DELETE FROM tariff.metering_point_tariff_attachment");
        await conn.ExecuteAsync("DELETE FROM tariff.tariff_rate");
        await conn.ExecuteAsync("DELETE FROM tariff.grid_tariff");
        await conn.ExecuteAsync("DELETE FROM tariff.subscription");
        await conn.ExecuteAsync("DELETE FROM tariff.electricity_tax");
        await conn.ExecuteAsync("DELETE FROM settlement.erroneous_switch_reversal");
        await conn.ExecuteAsync("DELETE FROM metering.metering_data_history");
        await conn.ExecuteAsync("DELETE FROM metering.annual_consumption_tracker");
        await conn.ExecuteAsync("DELETE FROM metering.metering_data");
        await conn.ExecuteAsync("DELETE FROM metering.spot_price");
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

        // ── Phase 2: Customers ───────────────────────────────────────────
        var firstNames = new[] { "Anders", "Mette", "Lars", "Sofie", "Jens", "Camilla", "Mikkel", "Louise", "Henrik", "Anne",
            "Peter", "Maria", "Rasmus", "Ida", "Christian", "Emma", "Thomas", "Julie", "Martin", "Katrine",
            "Nikolaj", "Sara", "Kasper", "Cecilie", "Jonas", "Nanna", "Simon", "Laura", "Frederik", "Astrid" };
        var lastNames = new[] { "Hansen", "Jensen", "Nielsen", "Andersen", "Pedersen", "Christensen", "Larsen", "Sorensen",
            "Rasmussen", "Petersen", "Madsen", "Kristensen", "Olsen", "Thomsen", "Poulsen", "Johansen",
            "Knudsen", "Mortensen", "Moller", "Jakobsen" };
        var companyNames = new[] { "ApS", "A/S", "I/S", "K/S" };
        var companyPrefixes = new[] { "Nordic", "Dansk", "Green", "Sol", "Vind", "El", "Smart", "Digital", "Eco", "Nord" };
        var companySuffixes = new[] { "Energy", "Tech", "Solutions", "Service", "Group", "Power", "Systems", "Trading", "Design", "Consult" };

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

        var customerIds = new List<Guid>();
        int custIdx = 0;
        int gsrnCounter = 1;

        // 200 customers (mix of private and business)
        for (int i = 0; i < 200; i++)
        {
            bool isBusiness = i % 6 == 0;
            string name, cprCvr, contactType;
            if (isBusiness)
            {
                name = $"{companyPrefixes[rng.Next(companyPrefixes.Length)]} {companySuffixes[rng.Next(companySuffixes.Length)]} {companyNames[rng.Next(companyNames.Length)]}";
                cprCvr = $"{10000000 + i:D8}";
                contactType = "business";
            }
            else
            {
                name = $"{firstNames[rng.Next(firstNames.Length)]} {lastNames[rng.Next(lastNames.Length)]}";
                cprCvr = $"{1000000000L + i:D10}";
                contactType = "private";
            }
            var customerId = Guid.NewGuid();
            customerIds.Add(customerId);
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

        // 260 additional customers
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
            var customerId = Guid.NewGuid();
            customerIds.Add(customerId);
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

        // ── Phase 3: Portfolio (metering points, contracts, supply periods) ──
        var activeMeteringPoints = new List<string>();

        // One metering point + contract + supply period per customer
        for (int i = 0; i < customerIds.Count; i++)
        {
            var gsrn = MakeGsrn(gsrnCounter++);
            var ga = PickGridArea(i);

            await conn.ExecuteAsync(
                "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area) VALUES (@Gsrn, 'E17', 'flex', 'connected', @GridArea, @Gln, @PriceArea)",
                new { Gsrn = gsrn, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga) });

            await conn.ExecuteAsync(
                "INSERT INTO portfolio.contract (id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date) VALUES (@Id, @CustomerId, @Gsrn, @ProductId, 'monthly', 'aconto', @Start)",
                new { Id = Guid.NewGuid(), CustomerId = customerIds[i], Gsrn = gsrn, ProductId = productIds[i % productIds.Count], Start = new DateTime(2025, 1, 1) });

            await conn.ExecuteAsync(
                "INSERT INTO portfolio.supply_period (id, gsrn, start_date) VALUES (@Id, @Gsrn, @Start)",
                new { Id = Guid.NewGuid(), Gsrn = gsrn, Start = new DateTime(2025, 1, 1) });

            activeMeteringPoints.Add(gsrn);
        }

        // 10 multi-MP customers (second metering point for the first 10 customers)
        for (int i = 0; i < 10; i++)
        {
            var gsrn2 = MakeGsrn(gsrnCounter++);
            var ga = PickGridArea(i);
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area) VALUES (@Gsrn, 'E17', 'flex', 'connected', @GridArea, @Gln, @PriceArea)",
                new { Gsrn = gsrn2, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga) });
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.contract (id, customer_id, gsrn, product_id, billing_frequency, payment_model, start_date) VALUES (@Id, @CustomerId, @Gsrn, @ProductId, 'monthly', 'aconto', @Start)",
                new { Id = Guid.NewGuid(), CustomerId = customerIds[i], Gsrn = gsrn2, ProductId = productIds[i % productIds.Count], Start = new DateTime(2025, 1, 1) });
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.supply_period (id, gsrn, start_date) VALUES (@Id, @Gsrn, @Start)",
                new { Id = Guid.NewGuid(), Gsrn = gsrn2, Start = new DateTime(2025, 1, 1) });
            activeMeteringPoints.Add(gsrn2);
        }

        // ── Phase 3b: Payers ────────────────────────────────────────────
        var payerNames = new[] { "Boligforeningen Sjælland", "Dansk Industri ApS", "Nordisk Ejendomme A/S",
            "Hansen & Søn Holding", "Grøn Bolig K/S", "Energi Danmark A/S", "Vestjysk Boligselskab",
            "København Kommune", "Aarhus Boligforening", "Norden Facility I/S" };
        var payerIds = new List<Guid>();

        for (int i = 0; i < 10; i++)
        {
            var payerId = Guid.NewGuid();
            payerIds.Add(payerId);
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

        // Link ~20 contracts to payers (every 10th metering point)
        for (int i = 0; i < activeMeteringPoints.Count && i < 200; i += 10)
        {
            var payerIdx = (i / 10) % payerIds.Count;
            await conn.ExecuteAsync(
                "UPDATE portfolio.contract SET payer_id = @PayerId WHERE gsrn = @Gsrn",
                new { PayerId = payerIds[payerIdx], Gsrn = activeMeteringPoints[i] });
        }

        // 5 disconnected metering points
        for (int i = 0; i < 5; i++)
        {
            var gsrn = MakeGsrn(gsrnCounter++);
            var ga = PickGridArea(i);
            await conn.ExecuteAsync(
                "INSERT INTO portfolio.metering_point (gsrn, type, settlement_method, connection_status, grid_area_code, grid_operator_gln, price_area, deactivated_at) VALUES (@Gsrn, 'E17', 'flex', 'disconnected', @GridArea, @Gln, @PriceArea, @DeactivatedAt)",
                new { Gsrn = gsrn, GridArea = ga, Gln = GridGln(ga), PriceArea = GridPriceArea(ga), DeactivatedAt = DateTime.UtcNow.AddDays(-rng.Next(30, 180)) });
        }

        // ── Phase 4: Tariffs ─────────────────────────────────────────────

        // Electricity tax (national rate)
        await conn.ExecuteAsync(
            "INSERT INTO tariff.electricity_tax (rate_per_kwh, valid_from, description) VALUES (0.008, '2025-01-01', 'Reduced rate 2025') ON CONFLICT (valid_from) DO UPDATE SET rate_per_kwh = 0.008");

        // Grid tariffs + subscriptions per grid area
        foreach (var ga in gridAreas)
        {
            // Grid tariff (time-of-use hourly rates)
            var gridTariffId = await conn.QuerySingleAsync<Guid>(
                "INSERT INTO tariff.grid_tariff (grid_area_code, charge_owner_id, tariff_type, valid_from) VALUES (@Code, @Gln, 'grid', '2025-01-01') ON CONFLICT (grid_area_code, tariff_type, valid_from) DO UPDATE SET charge_owner_id = EXCLUDED.charge_owner_id RETURNING id",
                new { ga.Code, ga.Gln });

            var gridRates = new List<object>();
            for (int h = 1; h <= 24; h++)
            {
                var price = h switch
                {
                    >= 1 and <= 6 => 0.15m,
                    >= 7 and <= 12 => 0.21m,
                    >= 13 and <= 16 => 0.25m,
                    >= 17 and <= 20 => 0.40m,
                    >= 21 and <= 22 => 0.25m,
                    _ => 0.15m,
                };
                gridRates.Add(new { GridTariffId = gridTariffId, HourNumber = h, PricePerKwh = price });
            }
            await conn.ExecuteAsync(
                "INSERT INTO tariff.tariff_rate (grid_tariff_id, hour_number, price_per_kwh) VALUES (@GridTariffId, @HourNumber, @PricePerKwh) ON CONFLICT (grid_tariff_id, hour_number) DO UPDATE SET price_per_kwh = EXCLUDED.price_per_kwh",
                gridRates);

            // System tariff (flat rate per hour)
            var systemTariffId = await conn.QuerySingleAsync<Guid>(
                "INSERT INTO tariff.grid_tariff (grid_area_code, charge_owner_id, tariff_type, valid_from) VALUES (@Code, @Gln, 'system', '2025-01-01') ON CONFLICT (grid_area_code, tariff_type, valid_from) DO UPDATE SET charge_owner_id = EXCLUDED.charge_owner_id RETURNING id",
                new { ga.Code, ga.Gln });

            var systemRates = Enumerable.Range(1, 24).Select(h => new { GridTariffId = systemTariffId, HourNumber = h, PricePerKwh = 0.054m }).ToList();
            await conn.ExecuteAsync(
                "INSERT INTO tariff.tariff_rate (grid_tariff_id, hour_number, price_per_kwh) VALUES (@GridTariffId, @HourNumber, @PricePerKwh) ON CONFLICT (grid_tariff_id, hour_number) DO UPDATE SET price_per_kwh = EXCLUDED.price_per_kwh",
                systemRates);

            // Transmission tariff (flat rate per hour)
            var transmissionTariffId = await conn.QuerySingleAsync<Guid>(
                "INSERT INTO tariff.grid_tariff (grid_area_code, charge_owner_id, tariff_type, valid_from) VALUES (@Code, @Gln, 'transmission', '2025-01-01') ON CONFLICT (grid_area_code, tariff_type, valid_from) DO UPDATE SET charge_owner_id = EXCLUDED.charge_owner_id RETURNING id",
                new { ga.Code, ga.Gln });

            var transmissionRates = Enumerable.Range(1, 24).Select(h => new { GridTariffId = transmissionTariffId, HourNumber = h, PricePerKwh = 0.049m }).ToList();
            await conn.ExecuteAsync(
                "INSERT INTO tariff.tariff_rate (grid_tariff_id, hour_number, price_per_kwh) VALUES (@GridTariffId, @HourNumber, @PricePerKwh) ON CONFLICT (grid_tariff_id, hour_number) DO UPDATE SET price_per_kwh = EXCLUDED.price_per_kwh",
                transmissionRates);

            await conn.ExecuteAsync(
                "INSERT INTO tariff.subscription (grid_area_code, subscription_type, amount_kr_per_month, valid_from) VALUES (@Code, 'grid', 49.00, '2025-01-01') ON CONFLICT DO NOTHING",
                new { ga.Code });
        }
    }
}
