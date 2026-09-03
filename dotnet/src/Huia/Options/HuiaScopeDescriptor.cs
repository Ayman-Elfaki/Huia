namespace Huia.Options;

/// <summary>
/// Declarative description of an OAuth scope to seed for a tenant. The seeder translates this into an
/// OpenIddict scope, writing the tenant binding into <c>Properties["huia:tenant"]</c>.
/// </summary>
public sealed class HuiaScopeDescriptor : IHuiaOptionsSection
{
    private static readonly string[] Reserved =
        ["openid", "profile", "email", "roles", "offline_access", "address", "phone"];

    /// <summary>The scope name. Required, unique within a tenant, and not one of the standard OIDC scopes.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable name shown on the consent screen.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Longer description shown on the consent screen.</summary>
    public string? Description { get; set; }

    /// <summary>Resource identifiers (audiences) that requesting this scope grants access to.</summary>
    public IList<string> Resources { get; } = [];

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        var namePath = HuiaOptionsValidation.Combine(path, nameof(Name));
        errors.Require(!string.IsNullOrWhiteSpace(Name), namePath, "is required.");

        if (string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        errors.Require(IsValidScopeName(Name), namePath,
            "must contain only lower-case letters, digits, ':', '_' and '-'.");
        errors.Require(!Reserved.Contains(Name, StringComparer.Ordinal), namePath,
            $"'{Name}' is a standard OIDC scope and is always available; it must not be declared as a custom scope.");
    }

    /// <summary>Whether <paramref name="name"/> is a syntactically valid custom scope name.</summary>
    /// <param name="name">The candidate scope name.</param>
    /// <returns><see langword="true"/> if it contains only lower-case letters, digits, ':', '_' and '-'.</returns>
    public static bool IsValidScopeName(string name) =>
        !string.IsNullOrEmpty(name) &&
        name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or ':' or '_' or '-');
}
