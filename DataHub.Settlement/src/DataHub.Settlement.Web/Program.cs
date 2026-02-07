using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Infrastructure.AddressLookup;
using DataHub.Settlement.Infrastructure.Dashboard;
using DataHub.Settlement.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("SettlementDb")!;
builder.Services.AddSingleton(new DashboardQueryService(connectionString));
builder.Services.AddSingleton(new DemoDataSeeder(connectionString));
builder.Services.AddSingleton(new SimulationService(connectionString));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAddressLookupClient>(new StubAddressLookupClient());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
