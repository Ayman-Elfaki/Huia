namespace Huia.EntityFrameworkCore.Identity;

/// <summary>Entity backing an external (third-party) login — the EF Core-mapped form of
/// <see cref="Huia.Stores.HuiaUserLoginInfo"/>. Composite key (<see cref="LoginProvider"/>,
/// <see cref="ProviderKey"/>).</summary>
public sealed class HuiaUserLogin
{
    /// <summary>The provider's logical name (e.g. <c>"Google"</c>).</summary>
    public required string LoginProvider { get; set; }

    /// <summary>The user's unique id at the provider.</summary>
    public required string ProviderKey { get; set; }

    /// <summary>The provider's display name, for UI.</summary>
    public string? ProviderDisplayName { get; set; }

    /// <summary>The user this login is linked to.</summary>
    public required string UserId { get; set; }
}
