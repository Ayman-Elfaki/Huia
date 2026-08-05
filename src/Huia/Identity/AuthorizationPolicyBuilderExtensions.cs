using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;

namespace Huia.Identity;

/// <summary>
/// Extensions for expressing OpenIddict scope requirements in ASP.NET Core authorization policies.
/// </summary>
public static class AuthorizationPolicyBuilderExtensions
{
    /// <summary>
    /// Requires the caller's access token to have been granted <paramref name="scope"/> (or, if more than
    /// one is given, any one of them). OpenIddict packs all granted scopes into a single space-delimited
    /// <c>scope</c> claim, so the built-in <c>RequireClaim("scope", "x")</c> never matches — use this
    /// instead, which checks membership via <see cref="OpenIddictExtensions.HasScope(ClaimsPrincipal, string)"/>.
    /// </summary>
    public static AuthorizationPolicyBuilder RequireScope(this AuthorizationPolicyBuilder builder,
        params string[] scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope.Length == 0
            ? throw new ArgumentException(@"At least one scope must be specified.", nameof(scope))
            : builder.RequireAssertion(context => scope.Any(context.User.HasScope));
    }

    /// <summary>
    /// Requires the caller's access token to have been minted for <paramref name="audience"/> (or, if more
    /// than one is given, any one of them) — i.e. its <c>aud</c> claim contains that value, checked via
    /// <see cref="OpenIddictExtensions.HasAudience(System.Security.Claims.ClaimsPrincipal, string)"/>. Useful
    /// for segregating a subset of an API's endpoints to a more specific resource than the one required
    /// server-wide by <c>HuiaBuilder.HuiaServerOptions.RequireAudiences</c>, since that check only gates
    /// authentication, not any individual endpoint.
    /// </summary>
    /// <remarks>
    /// Named (and implemented) to match OpenIddict's own validation-side vocabulary for a token a resource
    /// server is inspecting, not its scope-descriptor-side one: a scope's <see cref="Scopes.ScopeOptions.Resources"/>
    /// (which OpenIddict itself calls <c>OpenIddictScopeDescriptor.Resources</c>) describes, at token-minting
    /// time, which resource servers a token granted that scope should be usable against. Once a token
    /// reaches a resource server and is locally validated, that same information is exposed back on the
    /// resulting <see cref="System.Security.Claims.ClaimsPrincipal"/> as its "audience" —
    /// <see cref="OpenIddictExtensions.GetAudiences(System.Security.Claims.ClaimsPrincipal)"/>/<c>HasAudience</c>,
    /// backed by a different private claim (<c>oi_aud</c>) than the scope-descriptor's own
    /// <c>GetResources</c>/<c>HasResource</c> (<c>oi_rsrc</c>) — so this method deliberately doesn't call
    /// itself <c>RequireResource</c>, even though "resource" is the more OpenIddict-idiomatic term for the
    /// scope-declaration half of the same feature.
    /// </remarks>
    public static AuthorizationPolicyBuilder RequireAudience(this AuthorizationPolicyBuilder builder,
        params string[] audience)
    {
        ArgumentNullException.ThrowIfNull(audience);

        return audience.Length == 0
            ? throw new ArgumentException("At least one audience must be specified.", nameof(audience))
            : builder.RequireAssertion(context => audience.Any(context.User.HasAudience));
    }
}