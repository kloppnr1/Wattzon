using DataHub.Settlement.Application.DataHub;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.DataHub;

public sealed class StubDataHubClient : IDataHubClient
{
    private readonly ILogger<StubDataHubClient> _logger;
    private bool _loggedOnce;

    public StubDataHubClient(ILogger<StubDataHubClient> logger)
    {
        _logger = logger;
    }

    public Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct)
    {
        if (!_loggedOnce)
        {
            _logger.LogInformation("StubDataHubClient active — all queues return empty");
            _loggedOnce = true;
        }

        return Task.FromResult<DataHubMessage?>(null);
    }

    public Task DequeueAsync(string messageId, CancellationToken ct) => Task.CompletedTask;

    public Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct)
    {
        return Task.FromResult(new DataHubResponse($"stub-corr-{Guid.NewGuid():N}", true, null));
    }
}
