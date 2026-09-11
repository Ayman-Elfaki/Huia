using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;

namespace Microsoft.AspNetCore.Authorization;

/// <summary>
/// Building blocks for host-defined authorization policies over Huia bearer tokens. Each also
/// <see cref="AuthorizationPolicyBuilder.RequireAuthenticatedUser"/> and pins the OpenIddict validation
/// scheme once, so an anonymous call is a 401 bearer challenge rather than a redirect to the login page.
/// </summary>
public static class HuiaAuthorizationPolicyBuilderExtensions
{
    /// <summary>Requires the <c>tenant</c> claim to be one of <paramref name="tenants"/>.</summary>
    /// <param name="builder">The policy builder.</param>
    /// <param name="tenants">The permitted tenant identifiers.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireTenants(this AuthorizationPolicyBuilder builder, params string[] tenants)
    {
        Prepare(builder);
        builder.RequireAssertion(context => context.User.FindAll("tenant").Any(c => tenants.Contains(c.Value, StringComparer.Ordinal)));
        return builder;
    }

    /// <summary>Requires the presenter (<c>azp</c>, else <c>client_id</c>) to be one of <paramref name="clients"/>.</summary>
    /// <param name="builder">The policy builder.</param>
    /// <param name="clients">The permitted client ids.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static AuthorizationPolicyBuilder RequirePresenter(this AuthorizationPolicyBuilder builder, params string[] clients)
    {
        Prepare(builder);
        builder.RequireAssertion(context =>
        {
            var presenter = context.User.FindFirst("azp")?.Value ?? context.User.FindFirst("client_id")?.Value;
            return presenter is not null && clients.Contains(presenter, StringComparer.Ordinal);
        });
        return builder;
    }

    /// <summary>Requires a literal <c>role</c> claim (not ASP.NET's role-claim type) equal to <paramref name="role"/>.</summary>
    /// <param name="builder">The policy builder.</param>
    /// <param name="role">The required role.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireRole(this AuthorizationPolicyBuilder builder, string role)
    {
        Prepare(builder);
        builder.RequireAssertion(context => context.User.FindAll("role").Any(c => string.Equals(c.Value, role, StringComparison.Ordinal)));
        return builder;
    }

    /// <summary>Requires an <c>aud</c> claim equal to <paramref name="audience"/>.</summary>
    /// <param name="builder">The policy builder.</param>
    /// <param name="audience">The required audience.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireAudience(this AuthorizationPolicyBuilder builder, string audience)
    {
        Prepare(builder);
        builder.RequireAssertion(context => context.User.FindAll("aud").Any(c => string.Equals(c.Value, audience, StringComparison.Ordinal)));
        return builder;
    }

    private static void Prepare(AuthorizationPolicyBuilder builder)
    {
        if (!builder.AuthenticationSchemes.Contains(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme))
        {
            builder.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            builder.RequireAuthenticatedUser();
        }
    }
}
