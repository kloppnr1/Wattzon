using System.Net;
using DataHub.Settlement.Application.Authentication;
using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Application.Lifecycle;
using DataHub.Settlement.Infrastructure.DataHub;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class ResilientDataHubClientTests
{
    private readonly FakeTokenProvider _tokenProvider = new();

    private ResilientDataHubClient CreateSut(IDataHubClient inner)
        => new(inner, _tokenProvider, NullLogger<ResilientDataHubClient>.Instance);

    [Fact]
    public async Task PeekAsync_passes_through_on_success()
    {
        var expected = new DataHubMessage("msg-1", "RSM-012", null, "{}");
        var inner = new ControllableClient { PeekResult = expected };
        var sut = CreateSut(inner);

        var result = await sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);

        result.Should().Be(expected);
        inner.PeekCallCount.Should().Be(1);
    }

    [Fact]
    public async Task PeekAsync_retries_once_on_401_and_invalidates_token()
    {
        var expected = new DataHubMessage("msg-1", "RSM-012", null, "{}");
        var callCount = 0;
        var inner = new ControllableClient
        {
            PeekHandler = () =>
            {
                callCount++;
                if (callCount == 1)
                    throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
                return expected;
            }
        };
        var sut = CreateSut(inner);

        var result = await sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);

        result.Should().Be(expected);
        callCount.Should().Be(2);
        _tokenProvider.InvalidateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task PeekAsync_retries_on_503_with_backoff()
    {
        var expected = new DataHubMessage("msg-1", "RSM-012", null, "{}");
        var callCount = 0;
        var inner = new ControllableClient
        {
            PeekHandler = () =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable);
                return expected;
            }
        };
        var sut = CreateSut(inner);

        var result = await sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);

        result.Should().Be(expected);
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task PeekAsync_throws_after_max_retries_exhausted()
    {
        var inner = new ControllableClient
        {
            PeekHandler = () =>
                throw new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable)
        };
        var sut = CreateSut(inner);

        var act = () => sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendRequestAsync_passes_through_on_success()
    {
        var expected = new DataHubResponse("corr-1", true, null);
        var inner = new ControllableClient { SendResult = expected };
        var sut = CreateSut(inner);

        var result = await sut.SendRequestAsync(ProcessTypes.SupplierSwitch, "{}", CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task DequeueAsync_passes_through_on_success()
    {
        var inner = new ControllableClient();
        var sut = CreateSut(inner);

        await sut.DequeueAsync("msg-1", CancellationToken.None);

        inner.DequeueCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Only_retries_401_once_then_propagates()
    {
        var callCount = 0;
        var inner = new ControllableClient
        {
            PeekHandler = () =>
            {
                callCount++;
                throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
            }
        };
        var sut = CreateSut(inner);

        var act = () => sut.PeekAsync(QueueName.Timeseries, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        // 401 is only retried once: first attempt triggers invalidation, second attempt is the retry,
        // then the final attempt (after retry loop) also fails
        _tokenProvider.InvalidateCallCount.Should().Be(1);
    }

    private sealed class FakeTokenProvider : IAuthTokenProvider
    {
        public int InvalidateCallCount;

        public Task<string> GetTokenAsync(CancellationToken ct)
            => Task.FromResult("fake-token");

        public void InvalidateToken()
            => Interlocked.Increment(ref InvalidateCallCount);
    }

    private sealed class ControllableClient : IDataHubClient
    {
        public DataHubMessage? PeekResult { get; init; }
        public DataHubResponse SendResult { get; init; } = new("corr-1", true, null);
        public Func<DataHubMessage?>? PeekHandler { get; init; }
        public int PeekCallCount;
        public int DequeueCallCount;

        public Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct)
        {
            Interlocked.Increment(ref PeekCallCount);
            if (PeekHandler is not null)
                return Task.FromResult(PeekHandler());
            return Task.FromResult(PeekResult);
        }

        public Task DequeueAsync(string messageId, CancellationToken ct)
        {
            Interlocked.Increment(ref DequeueCallCount);
            return Task.CompletedTask;
        }

        public Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct)
            => Task.FromResult(SendResult);
    }
}
