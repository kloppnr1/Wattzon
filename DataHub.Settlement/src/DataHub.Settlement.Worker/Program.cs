using DataHub.Settlement.Application.AddressLookup;
using DataHub.Settlement.Application.Authentication;
using DataHub.Settlement.Application.Billing;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Application.Metering;
using DataHub.Settlement.Application.Messaging;
using DataHub.Settlement.Application.Onboarding;
using DataHub.Settlement.Application.Parsing;
using DataHub.Settlement.Application.Portfolio;
using DataHub.Settlement.Application.Settlement;
using DataHub.Settlement.Application.Tariff;
using DataHub.Settlement.Domain;
using DataHub.Settlement.Infrastructure;
using DataHub.Settlement.Infrastructure.AddressLookup;
using DataHub.Settlement.Infrastructure.Authentication;
using DataHub.Settlement.Infrastructure.Billing;
using DataHub.Settlement.Infrastructure.Database;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.Infrastructure.Metering;
using DataHub.Settlement.Infrastructure.Onboarding;
using DataHub.Settlement.Infrastructure.Parsing;
using DataHub.Settlement.Infrastructure.Portfolio;
using DataHub.Settlement.Infrastructure.Settlement;
using DataHub.Settlement.Infrastructure.Tariff;
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
            .AddMeter(SettlementMetrics.MeterName)
            .AddRuntimeInstrumentation()
            .AddOtlpExporter();
    });

var connectionString = builder.Configuration.GetConnectionString("SettlementDb")
    ?? Environment.GetEnvironmentVariable("SETTLEMENT_DB_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Database connection string not configured. Set ConnectionStrings__SettlementDb or SETTLEMENT_DB_CONNECTION_STRING environment variable.");

// DataHub client: requires DataHub__BaseUrl to be configured
var dataHubBaseUrl = builder.Configuration["DataHub:BaseUrl"]
    ?? throw new InvalidOperationException("DataHub:BaseUrl is not configured. Set the DataHub__BaseUrl environment variable.");

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

// Core services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IAddressLookupClient, StubAddressLookupClient>();
builder.Services.AddSingleton<ICimParser, CimJsonParser>();
builder.Services.AddSingleton<IMeteringDataRepository>(new MeteringDataRepository(connectionString));
builder.Services.AddSingleton<ISpotPriceRepository>(new SpotPriceRepository(connectionString));
builder.Services.AddSingleton<ITariffRepository>(new TariffRepository(connectionString));
builder.Services.AddSingleton<IPortfolioRepository>(new PortfolioRepository(connectionString));
builder.Services.AddSingleton<IProcessRepository>(new ProcessRepository(connectionString));
builder.Services.AddSingleton<ISignupRepository>(new SignupRepository(connectionString));
builder.Services.AddSingleton<IMessageLog>(new MessageLog(connectionString));
builder.Services.AddSingleton<IMessageRepository>(new MessageRepository(connectionString));
builder.Services.AddSingleton<IBrsRequestBuilder, BrsRequestBuilder>();
builder.Services.AddSingleton<IOnboardingService, OnboardingService>();

// Billing services
builder.Services.AddSingleton<IAcontoPaymentRepository>(new AcontoPaymentRepository(connectionString));
builder.Services.AddSingleton<IInvoiceRepository>(new InvoiceRepository(connectionString));
builder.Services.AddSingleton<IPaymentRepository>(new PaymentRepository(connectionString));
builder.Services.AddSingleton<IInvoiceService, InvoiceService>();
builder.Services.AddSingleton<IPaymentAllocator>(sp =>
    new PaymentAllocator(connectionString, sp.GetRequiredService<ILogger<PaymentAllocator>>()));
builder.Services.AddSingleton<IPaymentMatchingService, PaymentMatchingService>();

// Settlement services
builder.Services.AddSingleton<ISettlementEngine, SettlementEngine>();
builder.Services.AddSingleton<IMeteringCompletenessChecker>(new MeteringCompletenessChecker(connectionString));
builder.Services.AddSingleton<ISettlementDataLoader, SettlementDataLoader>();
builder.Services.AddSingleton<ISettlementResultStore>(new SettlementResultStore(connectionString));

// Spot price fetching from Energi Data Service (energidataservice.dk)
builder.Services.AddHttpClient<EnergiDataServiceClient>();
builder.Services.AddSingleton<ISpotPriceProvider>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var logger = sp.GetRequiredService<ILogger<EnergiDataServiceClient>>();
    return new EnergiDataServiceClient(httpClient, logger);
});

// Custom metrics
builder.Services.AddSingleton<SettlementMetrics>();

// Effectuation service (transactional RSM-022 activation handler)
builder.Services.AddSingleton<EffectuationService>(sp =>
    new EffectuationService(
        connectionString,
        sp.GetRequiredService<IOnboardingService>(),
        sp.GetRequiredService<IInvoiceService>(),
        sp.GetRequiredService<IDataHubClient>(),
        sp.GetRequiredService<IBrsRequestBuilder>(),
        sp.GetRequiredService<IMessageRepository>(),
        sp.GetRequiredService<IClock>(),
        sp.GetRequiredService<ILogger<EffectuationService>>(),
        sp.GetRequiredService<SettlementTriggerService>()));

// Settlement trigger (shared between QueuePoller and SettlementOrchestration)
builder.Services.AddSingleton<SettlementTriggerService>();

// Message handlers (extracted from QueuePollerService for readability)
builder.Services.AddSingleton<MasterDataMessageHandler>();

// Background services
builder.Services.AddHostedService<QueuePollerService>();
builder.Services.AddHostedService<ProcessSchedulerService>();
builder.Services.AddHostedService<SettlementOrchestrationService>();
builder.Services.AddHostedService<SpotPriceFetchingService>();
builder.Services.AddHostedService<OverdueCheckService>();
builder.Services.AddHostedService(sp =>
    new InvoicingService(
        connectionString,
        sp.GetRequiredService<IInvoiceService>(),
        sp.GetRequiredService<IAcontoPaymentRepository>(),
        sp.GetRequiredService<IClock>(),
        sp.GetRequiredService<ILogger<InvoicingService>>()));

var host = builder.Build();

// Run database migrations before starting the host
var migrationLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrator");
DatabaseMigrator.Migrate(connectionString, migrationLogger);

host.Run();
