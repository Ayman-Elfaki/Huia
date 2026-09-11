using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Huia.Headless.Options;
using Huia.Identity;
using Huia.Keys;
using Huia.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;

namespace Huia.Headless.Services;

/// <summary>Default implementation of <see cref="IHuiaHeadlessTokenService"/>.</summary>
public sealed class HuiaHeadlessTokenService(
    IHuiaKeyRing keyRing,
    HuiaOptions options,
    IHuiaHeadlessRefreshTokenStore refreshTokenStore,
    HuiaUserManager userManager,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : IHuiaHeadlessTokenService
{
    private readonly IDataProtector _provisionalProtector =
        dataProtectionProvider.CreateProtector("Huia.Headless.Provisional.v1");

    /// <inheritdoc />
    public async Task<HuiaTokenResponse> CreateTokenResponseAsync(
        string tenantId, HuiaUser user, string authenticationMethod, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(user);

        options.Tenants.TryGetValue(tenantId, out var tenantConfig);
        var headlessConfig = tenantConfig?.GetHuiaHeadless();

        var accessLifetime = headlessConfig?.AccessToken.Lifetime ?? TimeSpan.FromMinutes(15);
        var refreshLifetime = headlessConfig?.RefreshToken.Lifetime ?? TimeSpan.FromDays(30);

        var activeKey = await keyRing.GetActiveSigningKeyAsync(tenantId, cancellationToken);
        if (activeKey.SigningCredentials is null)
        {
            throw new InvalidOperationException($"No active signing credentials available for tenant '{tenantId}'.");
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(accessLifetime);

        var issuer = options.Issuer is not null
            ? $"{options.Issuer.AbsoluteUri.TrimEnd('/')}/{tenantId}"
            : $"https://huia.local/{tenantId}";
        var audience = options.Issuer?.AbsoluteUri.TrimEnd('/') ?? "https://huia.local";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(HuiaConstants.ClaimTypes.Tenant, tenantId),
            new("tenant", tenantId),
            new(HuiaConstants.ClaimTypes.AuthenticationMethod, authenticationMethod),
            new("amr", authenticationMethod),
        };

        if (!string.IsNullOrEmpty(user.UserName))
        {
            claims.Add(new(JwtRegisteredClaimNames.UniqueName, user.UserName));
        }

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new(JwtRegisteredClaimNames.Email, user.Email));
            claims.Add(new("email_verified", user.EmailConfirmed ? "true" : "false", ClaimValueTypes.Boolean));
        }

        if (!string.IsNullOrEmpty(user.PhoneNumber))
        {
            claims.Add(new("phone_number", user.PhoneNumber));
            claims.Add(new("phone_number_verified", user.PhoneNumberConfirmed ? "true" : "false", ClaimValueTypes.Boolean));
        }

        var roles = await userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            claims.Add(new(ClaimTypes.Role, role));
            claims.Add(new("role", role));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = audience,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = activeKey.SigningCredentials,
        };

        var handler = new JwtSecurityTokenHandler();
        var securityToken = handler.CreateToken(tokenDescriptor);
        var accessToken = handler.WriteToken(securityToken);

        var (rawRefreshToken, _) = await refreshTokenStore.IssueAsync(
            tenantId, user.Id, refreshLifetime, cancellationToken);

        return new HuiaTokenResponse
        {
            TokenType = HuiaHeadlessConstants.TokenTypes.Bearer,
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            ExpiresIn = (int)accessLifetime.TotalSeconds,
        };
    }

    /// <inheritdoc />
    public string CreateProvisionalToken(string tenantId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var payload = new ProvisionalPayload
        {
            TenantId = tenantId,
            UserId = userId,
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(15),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Base64UrlEncoder.Encode(_provisionalProtector.Protect(bytes));
    }

    /// <inheritdoc />
    public (string TenantId, string UserId)? ValidateProvisionalToken(string provisionalToken)
    {
        if (string.IsNullOrWhiteSpace(provisionalToken))
        {
            return null;
        }

        try
        {
            var protectedBytes = Base64UrlEncoder.DecodeBytes(provisionalToken);
            var unprotected = _provisionalProtector.Unprotect(protectedBytes);
            var payload = JsonSerializer.Deserialize<ProvisionalPayload>(unprotected);

            if (payload is null || payload.ExpiresAt <= timeProvider.GetUtcNow())
            {
                return null;
            }

            return (payload.TenantId, payload.UserId);
        }
        catch
        {
            return null;
        }
    }

    private sealed class ProvisionalPayload
    {
        public string TenantId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
