using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DataHub.Settlement.Application.Authentication;

namespace DataHub.Settlement.Infrastructure.Authentication;

public sealed class OAuth2TokenProvider : IAuthTokenProvider
{
    private static readonly TimeSpan ProactiveRenewalBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly AuthTokenOptions _options;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string? _cachedToken;
    private DateTime _expiresAtUtc;

    public OAuth2TokenProvider(HttpClient httpClient, AuthTokenOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTime.UtcNow.Add(ProactiveRenewalBuffer) < _expiresAtUtc)
            return _cachedToken;

        await _semaphore.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the semaphore
            if (_cachedToken is not null && DateTime.UtcNow.Add(ProactiveRenewalBuffer) < _expiresAtUtc)
                return _cachedToken;

            var tokenUrl = $"https://login.microsoftonline.com/{_options.TenantId}/oauth2/v2.0/token";

            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = $"{_options.Scope}/.default",
            });

            var response = await _httpClient.PostAsync(tokenUrl, body, ct);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            _cachedToken = tokenResponse!.AccessToken;
            _expiresAtUtc = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            return _cachedToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public required int ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public required string TokenType { get; init; }
    }
}
