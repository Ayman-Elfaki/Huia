using Finbuckle.MultiTenant.Abstractions;
using Huia.Keys;
using Huia.Multitenancy;
using Huia.Options;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Huia.OpenId.OpenIddict.Handlers;

/// <summary>
/// The server-feature counterpart of <see cref="HuiaTenantTokenValidationHandler"/>. The server's own
/// token-consuming endpoints (<c>userinfo</c>, <c>introspection</c>, <c>revocation</c>) validate with
/// only the ephemeral fallback key; this widens them with the tenant's published keys and issuer.
/// </summary>
/// <remarks>Order <c>int.MinValue + 101_000</c>: between <c>ResolveTokenValidationParameters</c> (+100_000) and <c>ValidateIdentityModelToken</c> (+103_000).</remarks>
internal sealed class HuiaTenantServerTokenValidationHandler(
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaKeyRing keyRing,
    HuiaOptions options) : IOpenIddictServerHandler<ValidateTokenContext>
{
    /// <summary>The descriptor that registers this handler in the server pipeline.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseScopedHandler<HuiaTenantServerTokenValidationHandler>()
            .SetOrder(int.MinValue + 101_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ValidateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var widened = await TenantTokenValidationParameters.WidenAsync(
            context.TokenValidationParameters, tenantAccessor.CurrentTenantId(), options, keyRing, context.CancellationToken);

        if (widened is not null)
        {
            context.TokenValidationParameters = widened;
        }
    }
}
