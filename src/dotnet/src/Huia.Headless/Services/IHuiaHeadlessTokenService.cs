using Huia.Identity;

namespace Huia.Headless.Services;

/// <summary>Represents a token response returned to headless API clients.</summary>
public sealed record HuiaTokenResponse
{
    /// <summary>The token type, always "Bearer".</summary>
    public string TokenType { get; init; } = HuiaHeadlessConstants.TokenTypes.Bearer;

    /// <summary>The signed JWT access token.</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>The revocable refresh token.</summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>Access token lifespan in seconds.</summary>
    public int ExpiresIn { get; init; }
}

/// <summary>Service for minting and issuing JWT access tokens and refresh tokens in Huia Headless.</summary>
public interface IHuiaHeadlessTokenService
{
    /// <summary>Creates a full token response (access token + refresh token) for a user.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="user">The user entity.</param>
    /// <param name="authenticationMethod">The primary authentication method used (e.g. password, sms).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token response.</returns>
    Task<HuiaTokenResponse> CreateTokenResponseAsync(
        string tenantId, HuiaUser user, string authenticationMethod, CancellationToken cancellationToken = default);

    /// <summary>Creates a provisional token for completing profile setup on first phone sign-in.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="userId">The pending user identifier.</param>
    /// <returns>An encrypted/signed provisional token.</returns>
    string CreateProvisionalToken(string tenantId, string userId);

    /// <summary>Validates a provisional token and returns the tenant and user identifiers.</summary>
    /// <param name="provisionalToken">The provisional token string.</param>
    /// <returns>Tuple of tenantId and userId if valid, or null.</returns>
    (string TenantId, string UserId)? ValidateProvisionalToken(string provisionalToken);
}
