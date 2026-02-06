using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Parsing;
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

builder.Services.AddSingleton<IDataHubClient, StubDataHubClient>();
builder.Services.AddSingleton<ICimParser, CimJsonParser>();
builder.Services.AddSingleton<IMeteringDataRepository>(new MeteringDataRepository(connectionString));
builder.Services.AddSingleton<IMessageLog>(new MessageLog(connectionString));
builder.Services.AddHostedService<QueuePollerService>();

var host = builder.Build();

// Run database migrations before starting the host
var migrationLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrator");
DatabaseMigrator.Migrate(connectionString, migrationLogger);

host.Run();
