namespace Huia.Scopes;

/// <summary>
/// Declaratively registers the OAuth 2.0/OIDC scopes Huia should seed into the OpenIddict scope store on
/// startup — each client application's own <c>AllowScopes(...)</c> only lists which scope names it may
/// request; this is what actually creates the scope (with its display metadata and resources) so it exists to
/// be granted at all. Reachable via <c>HuiaOptions.Scopes</c>.
/// </summary>
public sealed class ScopesBuilder
{
    private readonly List<ScopeOptions> _scopes = [];

    internal IReadOnlyList<ScopeOptions> Scopes => _scopes;

    /// <summary>
    /// Declares a scope named <paramref name="name"/> to seed on startup. Calling this again for a name
    /// already declared replaces the earlier declaration instead of adding a duplicate.
    /// </summary>
    public ScopesBuilder Add(string name, Action<ScopeOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A scope name is required.", nameof(name));
        }

        var options = new ScopeOptions(name);
        configure?.Invoke(options);

        _scopes.RemoveAll(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        _scopes.Add(options);
        return this;
    }
}