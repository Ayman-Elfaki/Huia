using System.Text.RegularExpressions;

namespace Huia.Tenants;

/// <summary>Rules for the id of a runtime-defined tenant, which is also its base-path segment.</summary>
public static partial class HuiaTenantIds
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "connect", "identity", "manage", "health", "healthz", "ready", "live", ".well-known",
        "favicon.ico", "robots.txt", "static", "assets", "wwwroot", "_framework",
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex Pattern();

    /// <summary>
    /// Returns <c>true</c> when <paramref name="id"/> is a lower-case letters / digits / hyphen path segment of at
    /// most 63 characters that does not collide with a well-known route.
    /// </summary>
    public static bool IsValid(string? id) => id is not null && Pattern().IsMatch(id) && !Reserved.Contains(id);
}
