using Huia.Applications;

namespace Huia.Scopes;

/// <summary>
/// Declaratively configures a single OAuth 2.0/OIDC scope Huia should seed into the OpenIddict scope store on
/// startup — its display metadata and, most importantly, the resources a token granted this scope should
/// carry on its <c>aud</c> claim. Reachable via <c>HuiaOptions.Scopes.Add(name, ...)</c>.
/// </summary>
public sealed class ScopeOptions
{
    /// <summary>The scope's identifier — what clients request and what appears in a token's <c>scope</c> claim.</summary>
    public string Name { get; }

    /// <summary>A human-readable name for this scope, e.g. shown on a consent screen.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>A longer, human-readable explanation of what this scope grants.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// The resources a token granted this scope carries on its <c>aud</c> claim — matches OpenIddict's own
    /// naming for a scope's resource-server associations (see <c>OpenIddictScopeDescriptor.Resources</c>).
    /// Leave unset for a scope that only gates which claims/permissions are granted, without asserting the
    /// token was issued for any particular resource server. Pair with
    /// <see cref="ApplicationsOptions.RequireAudiences"/> on whichever resource server
    /// should actually enforce one of these being present — that check is named for OpenIddict's own
    /// validation-side <c>AddAudiences</c>/<c>Audiences</c>, a distinct (if related) concept from this
    /// scope-descriptor-side <c>Resources</c> list; see <c>AuthorizationPolicyBuilderExtensions.RequireAudience</c>
    /// for more on the split.
    /// </summary>
    public IReadOnlyList<string> Resources { get; private set; } = [];

    internal ScopeOptions(string name) => Name = name;

    /// <summary>Sets <see cref="DisplayName"/>.</summary>
    public void SetDisplayName(string displayName) => DisplayName = displayName;

    /// <summary>Sets <see cref="Description"/>.</summary>
    public void SetDescription(string description) => Description = description;

    /// <summary>Sets <see cref="Resources"/>.</summary>
    public void SetResource(params string[] resources) => Resources = resources;
}