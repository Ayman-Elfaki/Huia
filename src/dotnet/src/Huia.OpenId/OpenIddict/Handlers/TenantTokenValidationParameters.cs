using Huia.Keys;
using Huia.Options;
using Microsoft.IdentityModel.Tokens;

namespace Huia.OpenId.OpenIddict.Handlers;

/// <summary>
/// Shared logic for the two token-validation handlers: take the parameters OpenIddict resolved (which
/// only know the server's ephemeral fallback key and the bare issuer) and widen them with the tenant's
/// published keys and per-tenant issuer.
/// </summary>
internal static class TenantTokenValidationParameters
{
    public static async ValueTask<TokenValidationParameters?> WidenAsync(
        TokenValidationParameters? source,
        string? tenantId,
        HuiaOptions options,
        IHuiaKeyRing keyRing,
        CancellationToken cancellationToken)
    {
        if (source is null || string.IsNullOrEmpty(tenantId) || options.Issuer is null)
        {
            return null;
        }

        var tenantKeys = await keyRing.GetPublishedKeysAsync(tenantId, cancellationToken);
        if (tenantKeys.Count == 0)
        {
            return null;
        }

        var clone = source.Clone();

        var signingKeys = new List<SecurityKey>();
        if (clone.IssuerSigningKeys is not null)
        {
            signingKeys.AddRange(clone.IssuerSigningKeys);
        }

        if (clone.IssuerSigningKey is not null)
        {
            signingKeys.Add(clone.IssuerSigningKey);
        }

        signingKeys.AddRange(tenantKeys.Select(k => k.SecurityKey));
        clone.IssuerSigningKeys = signingKeys;

        var tenantIssuer = $"{options.Issuer.AbsoluteUri.TrimEnd('/')}/{tenantId}";
        var issuers = new List<string>();
        if (clone.ValidIssuers is not null)
        {
            issuers.AddRange(clone.ValidIssuers);
        }

        if (!string.IsNullOrEmpty(clone.ValidIssuer))
        {
            issuers.Add(clone.ValidIssuer);
        }

        issuers.Add(tenantIssuer);
        clone.ValidIssuers = issuers.Distinct(StringComparer.Ordinal).ToArray();
        clone.ValidIssuer = null;

        return clone;
    }
}
