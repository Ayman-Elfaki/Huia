namespace Huia.Stores;

/// <summary>
/// Query model for <see cref="IHuiaStore{TUser,TRole}.ListUsersAsync"/>.
/// Supports both keyset pagination (via <see cref="After"/>/<see cref="Before"/>) and
/// offset pagination (via <see cref="Page"/>). Stores that only implement one style
/// should ignore the other set of fields.
/// </summary>
public sealed record HuiaUserQuery
{
    /// <summary>Filter to a specific tenant. <c>null</c> means all tenants (cross-tenant admin view).</summary>
    public string? TenantId { get; init; }

    /// <summary>Free-text search applied to email, username, first name, last name, and phone number.</summary>
    public string? Search { get; init; }

    /// <summary>Maximum number of items per page. Defaults to 25, clamped 1–100.</summary>
    public int PageSize { get; init; } = 25;

    /// <summary>Keyset cursor: return items after this opaque token.</summary>
    public string? After { get; init; }

    /// <summary>Keyset cursor: return items before this opaque token.</summary>
    public string? Before { get; init; }

    /// <summary>1-based page number for offset pagination. Ignored when <see cref="After"/> or <see cref="Before"/> is set.</summary>
    public int Page { get; init; } = 1;
}

/// <summary>
/// Query model for <see cref="IHuiaStore{TUser,TRole}.ListRolesAsync"/>.
/// </summary>
public sealed record HuiaRoleQuery
{
    /// <summary>Filter to a specific tenant. <c>null</c> means all tenants.</summary>
    public string? TenantId { get; init; }

    /// <summary>Maximum number of items per page. Defaults to 25, clamped 1–100.</summary>
    public int PageSize { get; init; } = 25;

    /// <summary>Keyset cursor: return items after this opaque token.</summary>
    public string? After { get; init; }

    /// <summary>Keyset cursor: return items before this opaque token.</summary>
    public string? Before { get; init; }

    /// <summary>1-based page number for offset pagination. Ignored when <see cref="After"/> or <see cref="Before"/> is set.</summary>
    public int Page { get; init; } = 1;
}
