using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.Authentication;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Infrastructure.AddressLookup;
using DataHub.Settlement.Infrastructure.Authentication;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Parsing;
using DataHub.Settlement.Infrastructure.Portfolio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

var serviceName = "DataHub.Settlement.Worker";

// OpenTelemetry: logs, traces, metrics → Aspire Dashboard via OTLP
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    logging.AddOtlpExporter();
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(serviceName)
            .AddNpgsql()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddRuntimeInstrumentation()
            .AddOtlpExporter();
    });

var connectionString = builder.Configuration.GetConnectionString("SettlementDb")
    ?? "Host=localhost;Port=5432;Database=datahub_settlement;Username=settlement;Password=settlement";

// DataHub client: use HTTP + resilience when configured, otherwise stub
var dataHubBaseUrl = builder.Configuration["DataHub:BaseUrl"];
if (!string.IsNullOrEmpty(dataHubBaseUrl))
{
    builder.Services.AddHttpClient<HttpDataHubClient>(client =>
    {
        client.BaseAddress = new Uri(dataHubBaseUrl);
    });

    builder.Services.AddHttpClient<OAuth2TokenProvider>();

    builder.Services.AddSingleton<IAuthTokenProvider>(sp =>
    {
        var options = new AuthTokenOptions(
            builder.Configuration["DataHub:TenantId"] ?? "",
            builder.Configuration["DataHub:ClientId"] ?? "",
            builder.Configuration["DataHub:ClientSecret"] ?? "",
            builder.Configuration["DataHub:Scope"] ?? "");
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        return new OAuth2TokenProvider(httpClient, options);
    });

    builder.Services.AddSingleton<IDataHubClient>(sp =>
    {
        var innerHttpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        innerHttpClient.BaseAddress = new Uri(dataHubBaseUrl);
        var inner = new HttpDataHubClient(innerHttpClient);
        var tokenProvider = sp.GetRequiredService<IAuthTokenProvider>();
        var logger = sp.GetRequiredService<ILogger<ResilientDataHubClient>>();
        return new ResilientDataHubClient(inner, tokenProvider, logger);
    });
}
else
{
    builder.Services.AddSingleton<IDataHubClient, StubDataHubClient>();
}

builder.Services.AddSingleton<IAddressLookupClient, StubAddressLookupClient>();
builder.Services.AddSingleton<ICimParser, CimJsonParser>();
builder.Services.AddSingleton<IMeteringDataRepository>(new MeteringDataRepository(connectionString));
builder.Services.AddSingleton<IPortfolioRepository>(new PortfolioRepository(connectionString));
builder.Services.AddSingleton<IMessageLog>(new MessageLog(connectionString));
builder.Services.AddHostedService<QueuePollerService>();

var host = builder.Build();

// Run database migrations before starting the host
var migrationLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrator");
DatabaseMigrator.Migrate(connectionString, migrationLogger);

host.Run();
