namespace Huia.Sessions;

/// <summary>
/// Configures Huia's user-session tracking. See <see cref="UserSessionsBuilder"/>.
/// </summary>
public sealed class UserSessionsOptions
{
    /// <summary>
    /// How long a session stays valid after creation, regardless of activity. Default: 14 days.
    /// </summary>
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromDays(14);
}