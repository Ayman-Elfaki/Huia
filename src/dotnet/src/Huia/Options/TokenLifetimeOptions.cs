namespace Huia.Options;

/// <summary>
/// Per-client token lifetime overrides. Any value left <see langword="null"/> falls back to the
/// OpenIddict server default. Mapped onto the OpenIddict application's <c>Settings</c> during seeding.
/// </summary>
public sealed class TokenLifetimeOptions : IHuiaOptionsSection
{
    /// <summary>Access token lifetime.</summary>
    public TimeSpan? AccessToken { get; set; }

    /// <summary>Identity token lifetime.</summary>
    public TimeSpan? IdentityToken { get; set; }

    /// <summary>Refresh token lifetime.</summary>
    public TimeSpan? RefreshToken { get; set; }

    /// <summary>Authorization code lifetime.</summary>
    public TimeSpan? AuthorizationCode { get; set; }

    /// <summary>Device code lifetime.</summary>
    public TimeSpan? DeviceCode { get; set; }

    /// <summary>User code lifetime.</summary>
    public TimeSpan? UserCode { get; set; }

    /// <summary>Whether any override has been set.</summary>
    public bool HasAny =>
        AccessToken is not null || IdentityToken is not null || RefreshToken is not null ||
        AuthorizationCode is not null || DeviceCode is not null || UserCode is not null;

    internal TokenLifetimeOptions Clone() => new()
    {
        AccessToken = AccessToken,
        IdentityToken = IdentityToken,
        RefreshToken = RefreshToken,
        AuthorizationCode = AuthorizationCode,
        DeviceCode = DeviceCode,
        UserCode = UserCode,
    };

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        foreach (var (name, value) in Enumerate())
        {
            if (value is { } span)
            {
                errors.Require(span > TimeSpan.Zero, HuiaOptionsValidation.Combine(path, name), "must be positive.");
            }
        }
    }

    private IEnumerable<(string Name, TimeSpan? Value)> Enumerate()
    {
        yield return (nameof(AccessToken), AccessToken);
        yield return (nameof(IdentityToken), IdentityToken);
        yield return (nameof(RefreshToken), RefreshToken);
        yield return (nameof(AuthorizationCode), AuthorizationCode);
        yield return (nameof(DeviceCode), DeviceCode);
        yield return (nameof(UserCode), UserCode);
    }
}
