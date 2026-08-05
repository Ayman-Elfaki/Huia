using System.Security.Claims;
using Huia.Identity;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.Tests.Unit.Authorization;

public class AuthorizationPolicyBuilderExtensionsTests
{
    [Fact]
    public async Task RequireScope_Succeeds_WhenPrincipalHasScope()
    {
        var principal = CreatePrincipalWithScopes("todos", "profile");
        var policy = new AuthorizationPolicyBuilder().RequireScope("todos").Build();

        Assert.True(await EvaluateAsync(policy, principal));
    }

    [Fact]
    public async Task RequireScope_Fails_WhenPrincipalMissingScope()
    {
        var principal = CreatePrincipalWithScopes("profile");
        var policy = new AuthorizationPolicyBuilder().RequireScope("todos").Build();

        Assert.False(await EvaluateAsync(policy, principal));
    }

    [Fact]
    public async Task RequireScope_Succeeds_WhenAnyOfMultipleScopesMatch()
    {
        var principal = CreatePrincipalWithScopes("billing");
        var policy = new AuthorizationPolicyBuilder().RequireScope("todos", "billing").Build();

        Assert.True(await EvaluateAsync(policy, principal));
    }

    [Fact]
    public void RequireScope_WithNoScopes_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AuthorizationPolicyBuilder().RequireScope());
    }

    [Fact]
    public async Task RequireAudience_Succeeds_WhenPrincipalHasAudience()
    {
        var principal = CreatePrincipalWithAudiences("todo-api", "reports-api");
        var policy = new AuthorizationPolicyBuilder().RequireAudience("reports-api").Build();

        Assert.True(await EvaluateAsync(policy, principal));
    }

    [Fact]
    public async Task RequireAudience_Fails_WhenPrincipalMissingAudience()
    {
        var principal = CreatePrincipalWithAudiences("todo-api");
        var policy = new AuthorizationPolicyBuilder().RequireAudience("reports-api").Build();

        Assert.False(await EvaluateAsync(policy, principal));
    }

    [Fact]
    public async Task RequireAudience_Succeeds_WhenAnyOfMultipleAudiencesMatch()
    {
        var principal = CreatePrincipalWithAudiences("billing-api");
        var policy = new AuthorizationPolicyBuilder().RequireAudience("reports-api", "billing-api").Build();

        Assert.True(await EvaluateAsync(policy, principal));
    }

    [Fact]
    public void RequireAudience_WithNoAudiences_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AuthorizationPolicyBuilder().RequireAudience());
    }

    /// <summary>
    /// A scope's <em>resources</em> (<c>ScopeOptions.Resources</c>, matching OpenIddict's own
    /// <c>OpenIddictScopeDescriptor.Resources</c>) are a distinct claim from what a validated principal
    /// exposes as its <em>audiences</em> (<c>GetAudiences</c>/<c>HasAudience</c>, backed by the private
    /// <c>oi_aud</c> claim, vs. resources' own <c>oi_rsrc</c>) — see the remarks on
    /// <see cref="AuthorizationPolicyBuilderExtensions.RequireAudience"/>. <c>Huia.Tests.Integration</c>'s
    /// <c>ReportsEndpointsTests</c> covers the full mint-to-validate path over a real token; this proves, at
    /// the unit level, that building a principal via <c>SetResources</c> alone (without also setting
    /// audiences) does *not* satisfy <c>RequireAudience</c> — the two are not interchangeable.
    /// </summary>
    [Fact]
    public async Task RequireAudience_Fails_WhenOnlyResourcesAreSet_NotAudiences()
    {
        var identity = new ClaimsIdentity(authenticationType: "Bearer", nameType: Claims.Name, roleType: Claims.Role);
        identity.SetResources("reports-api");
        var policy = new AuthorizationPolicyBuilder().RequireAudience("reports-api").Build();

        Assert.False(await EvaluateAsync(policy, new ClaimsPrincipal(identity)));
    }

    private static ClaimsPrincipal CreatePrincipalWithScopes(params string[] scopes)
    {
        var identity = new ClaimsIdentity(authenticationType: "Bearer", nameType: Claims.Name, roleType: Claims.Role);
        identity.SetScopes(scopes);
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreatePrincipalWithAudiences(params string[] audiences)
    {
        var identity = new ClaimsIdentity(authenticationType: "Bearer", nameType: Claims.Name, roleType: Claims.Role);
        identity.SetAudiences(audiences);
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> EvaluateAsync(AuthorizationPolicy policy, ClaimsPrincipal principal)
    {
        var context = new AuthorizationHandlerContext(policy.Requirements, principal, resource: null);

        foreach (var handler in policy.Requirements.OfType<IAuthorizationHandler>())
        {
            await handler.HandleAsync(context);
        }

        return context.HasSucceeded;
    }
}