using Finbuckle.MultiTenant.Abstractions;
using Huia.Keys;
using Huia.Multitenancy;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Huia.OpenId.OpenIddict.Handlers;

/// <summary>
/// Replaces the keys advertised at <c>/{tenant}/.well-known/jwks</c> with the requesting tenant's
/// published keys, so relying parties fetch the right verification material.
/// </summary>
/// <remarks>Late order so it runs after OpenIddict has populated its own (ephemeral) keys.</remarks>
internal sealed class HuiaTenantJwksHandler(IMultiTenantContextAccessor tenantAccessor, IHuiaKeyRing keyRing)
    : IOpenIddictServerHandler<HandleJsonWebKeySetRequestContext>
{
    /// <summary>The descriptor that registers this handler in the server pipeline.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<HandleJsonWebKeySetRequestContext>()
            .UseScopedHandler<HuiaTenantJwksHandler>()
            .SetOrder(int.MaxValue - 100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(HandleJsonWebKeySetRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tenantId = tenantAccessor.CurrentTenantId();
        if (tenantId is null)
        {
            return;
        }

        var jwks = await keyRing.GetPublishedJwksAsync(tenantId, context.CancellationToken);
        if (jwks.Count == 0)
        {
            return;
        }

        context.Keys.Clear();
        foreach (var jwk in jwks)
        {
            context.Keys.Add(new JsonWebKey
            {
                Kty = jwk.Kty,
                Use = jwk.Use ?? "sig",
                Kid = jwk.Kid,
                Alg = jwk.Alg ?? SecurityAlgorithms.RsaSha256,
                N = jwk.N,
                E = jwk.E,
            });
        }
    }
}
