using System.Net;
using System.Text;
using DataHub.Settlement.Application.Authentication;
using DataHub.Settlement.Infrastructure.Authentication;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.UnitTests;

public class OAuth2TokenProviderTests
{
    private static readonly AuthTokenOptions DefaultOptions = new(
        TenantId: "test-tenant",
        ClientId: "test-client-id",
        ClientSecret: "test-secret",
        Scope: "api://datahub");

    private static HttpResponseMessage MakeTokenResponse(string token = "test-token", int expiresIn = 3600)
    {
        var json = $$"""{"access_token":"{{token}}","expires_in":{{expiresIn}},"token_type":"Bearer"}""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task GetTokenAsync_fetches_token_from_endpoint()
    {
        using var handler = new FakeHttpMessageHandler
        {
            Handler = request =>
            {
                request.Method.Should().Be(HttpMethod.Post);
                request.RequestUri!.ToString().Should().Contain("test-tenant/oauth2/v2.0/token");

                var body = request.Content!.ReadAsStringAsync().Result;
                body.Should().Contain("grant_type=client_credentials");
                body.Should().Contain("client_id=test-client-id");
                body.Should().Contain("client_secret=test-secret");
                body.Should().Contain("scope=api%3A%2F%2Fdatahub%2F.default");

                return MakeTokenResponse("my-access-token");
            },
        };
        var sut = new OAuth2TokenProvider(new HttpClient(handler), DefaultOptions);

        var token = await sut.GetTokenAsync(CancellationToken.None);

        token.Should().Be("my-access-token");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Token_is_cached_on_subsequent_calls()
    {
        using var handler = new FakeHttpMessageHandler
        {
            Handler = _ => MakeTokenResponse("cached-token", expiresIn: 3600),
        };
        var sut = new OAuth2TokenProvider(new HttpClient(handler), DefaultOptions);

        var first = await sut.GetTokenAsync(CancellationToken.None);
        var second = await sut.GetTokenAsync(CancellationToken.None);

        first.Should().Be("cached-token");
        second.Should().Be("cached-token");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Expired_token_triggers_refresh()
    {
        var callCount = 0;
        using var handler = new FakeHttpMessageHandler
        {
            Handler = _ =>
            {
                callCount++;
                // First call: token that expires immediately (0 seconds)
                // Second call: fresh token
                return callCount == 1
                    ? MakeTokenResponse("old-token", expiresIn: 0)
                    : MakeTokenResponse("new-token", expiresIn: 3600);
            },
        };
        var sut = new OAuth2TokenProvider(new HttpClient(handler), DefaultOptions);

        var first = await sut.GetTokenAsync(CancellationToken.None);
        var second = await sut.GetTokenAsync(CancellationToken.None);

        first.Should().Be("old-token");
        second.Should().Be("new-token");
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Proactive_renewal_before_five_minute_window()
    {
        var callCount = 0;
        using var handler = new FakeHttpMessageHandler
        {
            Handler = _ =>
            {
                callCount++;
                // First call: token that expires in 4 minutes (within 5-min buffer)
                // Second call: fresh token
                return callCount == 1
                    ? MakeTokenResponse("expiring-token", expiresIn: 240)
                    : MakeTokenResponse("fresh-token", expiresIn: 3600);
            },
        };
        var sut = new OAuth2TokenProvider(new HttpClient(handler), DefaultOptions);

        var first = await sut.GetTokenAsync(CancellationToken.None);
        var second = await sut.GetTokenAsync(CancellationToken.None);

        first.Should().Be("expiring-token");
        second.Should().Be("fresh-token");
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_calls_only_fetch_once()
    {
        using var handler = new FakeHttpMessageHandler
        {
            Handler = _ =>
            {
                // Simulate a slow token endpoint
                Thread.Sleep(50);
                return MakeTokenResponse("concurrent-token", expiresIn: 3600);
            },
        };
        var sut = new OAuth2TokenProvider(new HttpClient(handler), DefaultOptions);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => sut.GetTokenAsync(CancellationToken.None))
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        tokens.Should().AllBe("concurrent-token");
        handler.CallCount.Should().Be(1);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount;
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; init; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult(Handler(request));
        }
    }
}
