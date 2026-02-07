using System.Net;
using DataHub.Settlement.Application.Authentication;
using DataHub.Settlement.Application.DataHub;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.DataHub;

public sealed class ResilientDataHubClient : IDataHubClient
{
    private readonly IDataHubClient _inner;
    private readonly IAuthTokenProvider _tokenProvider;
    private readonly ILogger<ResilientDataHubClient> _logger;

    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);

    public ResilientDataHubClient(
        IDataHubClient inner,
        IAuthTokenProvider tokenProvider,
        ILogger<ResilientDataHubClient> logger)
    {
        _inner = inner;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct)
        => ExecuteWithResilienceAsync(() => _inner.PeekAsync(queue, ct), nameof(PeekAsync), ct);

    public Task DequeueAsync(string messageId, CancellationToken ct)
        => ExecuteWithResilienceAsync(async () =>
        {
            await _inner.DequeueAsync(messageId, ct);
            return (object?)null;
        }, nameof(DequeueAsync), ct);

    public Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct)
        => ExecuteWithResilienceAsync(() => _inner.SendRequestAsync(processType, cimPayload, ct), nameof(SendRequestAsync), ct);

    private async Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationName, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _logger.LogWarning("DataHub returned 401 for {Operation} — invalidating token and retrying", operationName);
                _tokenProvider.InvalidateToken();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < MaxRetries)
            {
                var delay = InitialBackoff * Math.Pow(2, attempt);
                _logger.LogWarning("DataHub returned 503 for {Operation} — retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    operationName, delay.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(delay, ct);
            }
        }

        // Final attempt — let any exception propagate
        return await action();
    }
}
