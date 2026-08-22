namespace Huia.Identity;

/// <summary>A single validation/operation error from <see cref="HuiaUserManager"/>/<see cref="HuiaRoleManager"/>.
/// Replaces ASP.NET Core Identity's <c>IdentityError</c>.</summary>
public sealed class HuiaIdentityError
{
    /// <summary>A short, stable, machine-readable error code (e.g. <c>"DuplicateEmail"</c>).</summary>
    public required string Code { get; init; }

    /// <summary>A human-readable, localized description.</summary>
    public required string Description { get; init; }
}
