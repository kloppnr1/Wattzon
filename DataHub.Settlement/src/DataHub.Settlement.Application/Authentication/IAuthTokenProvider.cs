namespace DataHub.Settlement.Application.Authentication;

public interface IAuthTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
    void InvalidateToken();
}

public record AuthTokenOptions(
    string TenantId,
    string ClientId,
    string ClientSecret,
    string Scope);
