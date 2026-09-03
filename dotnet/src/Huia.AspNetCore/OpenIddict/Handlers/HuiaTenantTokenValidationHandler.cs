using Huia.AspNetCore.Keys;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Options;
using OpenIddict.Validation;
using static OpenIddict.Validation.OpenIddictValidationEvents;

namespace Huia.AspNetCore.OpenIddict.Handlers;

/// <summary>
/// Widens the validation feature's token parameters with the current tenant's published keys and
/// issuer, so bearer tokens minted with a rotated tenant key are accepted on the token-protected
/// (<c>/manage</c>, <c>/admin</c>) API surface.
/// </summary>
/// <remarks>
/// Order <c>int.MinValue + 101_000</c>: after <c>ResolveTokenValidationParameters</c> (+100_000) and
/// before <c>ValidateIdentityModelToken</c> (+103_000).
/// </remarks>
internal sealed class HuiaTenantTokenValidationHandler(
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaKeyRing keyRing,
    HuiaOptions options) : IOpenIddictValidationHandler<ValidateTokenContext>
{
    /// <summary>The descriptor that registers this handler in the validation pipeline.</summary>
    public static OpenIddictValidationHandlerDescriptor Descriptor { get; } =
        OpenIddictValidationHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseScopedHandler<HuiaTenantTokenValidationHandler>()
            .SetOrder(int.MinValue + 101_000)
            .SetType(OpenIddictValidationHandlerType.Custom)
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
