namespace Huia.Sessions;

/// <summary>
/// Configures Huia's user-session tracking. See <see cref="UserSessionsBuilder"/>.
/// </summary>
public sealed class UserSessionsOptions
{
    /// <summary>
    /// How long a session stays valid after creation, regardless of activity. Default: 10 hours
    /// </summary>
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(10);

    /// <summary>
    /// How long a session can go without activity before <see cref="UserSessionTimeoutJob"/> revokes it.
    /// Default: 30 minutes. See
    /// <see cref="UserSessionDescriptor.LastActivityAt"/> for what counts as activity.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);
}