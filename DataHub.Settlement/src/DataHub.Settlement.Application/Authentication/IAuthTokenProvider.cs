namespace DataHub.Settlement.Application.Authentication;

public interface IAuthTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
}

public record AuthTokenOptions(
    string TenantId,
    string ClientId,
    string ClientSecret,
    string Scope);
