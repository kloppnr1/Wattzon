using Dapper;
using Npgsql;

namespace DataHub.Settlement.Infrastructure.Database;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var products = new[]
        {
            (Name: "Spot Green", Model: "spot", Margin: 4.50m, Supp: 1.00m, Sub: 29.00m, Desc: "100% green electricity at spot prices", Green: true, Order: 1),
            (Name: "Spot Standard", Model: "spot", Margin: 3.00m, Supp: 0m, Sub: 19.00m, Desc: "Standard electricity at spot prices", Green: false, Order: 2),
            (Name: "Fixed Price", Model: "fixed_price", Margin: 8.00m, Supp: 0m, Sub: 39.00m, Desc: "Fixed price for 12 months", Green: false, Order: 3),
            (Name: "Mixed Green", Model: "mixed", Margin: 6.00m, Supp: 2.00m, Sub: 35.00m, Desc: "Blended spot + fixed with green certificates", Green: true, Order: 4),
        };

        foreach (var p in products)
        {
            var exists = await conn.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT id FROM portfolio.product WHERE name = @Name LIMIT 1", new { p.Name });

            if (!exists.HasValue || exists.Value == Guid.Empty)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO portfolio.product (id, name, energy_model, margin_ore_per_kwh, supplement_ore_per_kwh, subscription_kr_per_month, description, green_energy, display_order)
                    VALUES (@Id, @Name, @Model, @Margin, @Supp, @Sub, @Desc, @Green, @Order)
                    """,
                    new { Id = Guid.NewGuid(), p.Name, p.Model, p.Margin, p.Supp, p.Sub, p.Desc, p.Green, p.Order });
            }
        }
    }
}
