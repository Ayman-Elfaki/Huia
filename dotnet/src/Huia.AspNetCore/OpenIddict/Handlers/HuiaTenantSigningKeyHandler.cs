using Huia.AspNetCore.Keys;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Multitenancy;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Huia.AspNetCore.OpenIddict.Handlers;

/// <summary>
/// Signs access tokens and identity tokens with the requesting tenant's active rotated key instead of
/// the server's throw-away ephemeral key.
/// </summary>
/// <remarks>
/// Order <c>int.MinValue + 100_500</c>: after <c>AttachSecurityCredentials</c> (which sets the default
/// credentials) and before the token is actually minted. It must only touch access / identity tokens —
/// overriding the authorization-code or refresh-token credentials breaks the <c>code</c> exchange with
/// <c>invalid_grant</c> (ID2004), since OpenIddict protects those with its own keys.
/// </remarks>
internal sealed class HuiaTenantSigningKeyHandler(IMultiTenantContextAccessor tenantAccessor, IHuiaKeyRing keyRing)
    : IOpenIddictServerHandler<GenerateTokenContext>
{
    /// <summary>The descriptor that registers this handler in the server pipeline.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<GenerateTokenContext>()
            .UseScopedHandler<HuiaTenantSigningKeyHandler>()
            .SetOrder(int.MinValue + 100_500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(GenerateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.TokenType is not (TokenTypeIdentifiers.AccessToken or TokenTypeIdentifiers.IdentityToken))
        {
            return;
        }

        var tenantId = tenantAccessor.CurrentTenantId();
        if (tenantId is null)
        {
            return;
        }

        var key = await keyRing.GetActiveSigningKeyAsync(tenantId, context.CancellationToken);
        if (key.SigningCredentials is not null)
        {
            context.SigningCredentials = key.SigningCredentials;
        }
    }
}
