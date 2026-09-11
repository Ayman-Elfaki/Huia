using Huia.Options;

namespace Huia.Headless.Options;

/// <summary>
/// Tenant-level configuration for the Huia Headless API flavor (bearer tokens, CORS, lifetimes).
/// </summary>
public sealed class HuiaHeadlessTenantOptions : IHuiaOptionsSection
{
    /// <summary>Allowed CORS origins for cross-origin browser clients (SPAs).</summary>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>Access token lifetime configuration.</summary>
    public AccessTokenOptions AccessToken { get; } = new();

    /// <summary>Refresh token lifetime and sliding expiration configuration.</summary>
    public RefreshTokenOptions RefreshToken { get; } = new();

    /// <inheritdoc />
    public void Validate(string path, List<string> errors)
    {
        ((IHuiaOptionsSection)AccessToken).Validate(HuiaOptionsValidation.Combine(path, nameof(AccessToken)), errors);
        ((IHuiaOptionsSection)RefreshToken).Validate(HuiaOptionsValidation.Combine(path, nameof(RefreshToken)), errors);

        for (var i = 0; i < AllowedOrigins.Count; i++)
        {
            var origin = AllowedOrigins[i];
            if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out _))
            {
                errors.Add($"{HuiaOptionsValidation.Combine(path, $"AllowedOrigins[{i}]")}: '{origin}' must be an absolute URI.");
            }
        }
    }
}

/// <summary>Access token options for headless clients.</summary>
public sealed class AccessTokenOptions : IHuiaOptionsSection
{
    /// <summary>Access token lifespan. Defaults to 15 minutes.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public void Validate(string path, List<string> errors)
    {
        errors.Require(Lifetime > TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(Lifetime)), "must be positive.");
    }
}

/// <summary>Refresh token options for headless clients.</summary>
public sealed class RefreshTokenOptions : IHuiaOptionsSection
{
    /// <summary>Refresh token lifespan. Defaults to 30 days.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Whether rotation grants a fresh lifetime on each refresh. Defaults to true.</summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <inheritdoc />
    public void Validate(string path, List<string> errors)
    {
        errors.Require(Lifetime > TimeSpan.Zero, HuiaOptionsValidation.Combine(path, nameof(Lifetime)), "must be positive.");
    }
}
