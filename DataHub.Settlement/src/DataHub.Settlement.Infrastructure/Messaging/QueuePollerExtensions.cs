using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Messaging;

public static class QueuePollerExtensions
{
    public static IServiceCollection AddQueuePoller<THandler>(
        this IServiceCollection services, TimeSpan pollInterval)
        where THandler : class, IMessageHandler
    {
        services.AddSingleton<THandler>();
        services.AddSingleton(sp =>
        {
            var handler = sp.GetRequiredService<THandler>();
            var client = sp.GetRequiredService<IDataHubClient>();
            var messageLog = sp.GetRequiredService<IMessageLog>();
            var metrics = sp.GetRequiredService<SettlementMetrics>();
            var logger = sp.GetRequiredService<ILogger<QueuePoller<THandler>>>();

            return new QueuePoller<THandler>(client, handler, messageLog, metrics, logger, pollInterval);
        });
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<QueuePoller<THandler>>());

        return services;
    }
}
