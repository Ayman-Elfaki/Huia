namespace Huia.Sessions;

/// <summary>
/// Configures Huia's user-session tracking — one session per interactive sign-in, carried on tokens as the
/// <c>sid</c> claim, listable/revocable via the admin <c>/sessions</c> endpoints. Reachable via
/// <c>HuiaOptions.Sessions</c> inside <c>services.AddHuia(issuer, huia => {...})</c>.
/// </summary>
public sealed class UserSessionsBuilder
{
    internal UserSessionsOptions Options { get; } = new();

    /// <summary>
    /// Sets <see cref="UserSessionsOptions.AbsoluteLifetime"/>.
    /// </summary>
    public UserSessionsBuilder SetAbsoluteLifetime(TimeSpan lifetime)
    {
        Options.AbsoluteLifetime = lifetime;
        return this;
    }
}