using System.Collections.Concurrent;

namespace Huia.Headless.Services;

/// <summary>
/// Holds the result of an external-provider callback until the app's server exchanges the one-time
/// code for a bearer token (or, for a first-time sign-up, until it also supplies a first/last name).
/// The code is the only thing that ever reaches the browser — never a token — and is single-use and
/// short-lived, exactly like an OAuth authorization code.
/// </summary>
public sealed record ExternalLoginFlow(
    string Code,
    string? UserId,
    string? LoginProvider,
    ExternalSignup? PendingSignup);

/// <summary>Data carried forward from the provider callback for a first-time sign-up.</summary>
/// <param name="LoginProvider">The provider registration name (matches <c>ExternalProviderRegistration.Name</c>).</param>
/// <param name="ProviderKey">The provider's own subject identifier.</param>
/// <param name="ProviderDisplayName">A friendly provider label, for the login attached to the new account.</param>
/// <param name="Email">The provider-supplied email, if any.</param>
/// <param name="FirstName">A best-effort first name guess from the provider's claims.</param>
/// <param name="LastName">A best-effort last name guess from the provider's claims.</param>
public sealed record ExternalSignup(
    string LoginProvider,
    string ProviderKey,
    string ProviderDisplayName,
    string? Email,
    string? FirstName,
    string? LastName);

/// <summary>See <see cref="ExternalLoginFlow"/>.</summary>
public interface IExternalLoginFlowStore
{
    /// <summary>Creates a code for an account that resolved directly (existing or newly linked).</summary>
    /// <param name="userId">The account id.</param>
    /// <param name="loginProvider">The provider registration name the sign-in came through.</param>
    /// <returns>The new code.</returns>
    string CreateForUser(string userId, string loginProvider);

    /// <summary>Creates a code for a first-time sign-up still awaiting a first/last name.</summary>
    /// <param name="signup">The provider data to create the account from once a name is supplied.</param>
    /// <returns>The new code.</returns>
    string CreateForSignup(ExternalSignup signup);

    /// <summary>Gets a flow by its code, or <see langword="null"/> when unknown or expired.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The flow or <see langword="null"/>.</returns>
    ExternalLoginFlow? Get(string code);

    /// <summary>Consumes (and removes) a code, single-use.</summary>
    /// <param name="code">The code.</param>
    void Remove(string code);
}

/// <summary>In-memory <see cref="IExternalLoginFlowStore"/> with lazy expiry.</summary>
internal sealed class ExternalLoginFlowStore(TimeProvider timeProvider) : IExternalLoginFlowStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string CreateForUser(string userId, string loginProvider) => Create(userId, loginProvider, null);

    public string CreateForSignup(ExternalSignup signup) => Create(null, null, signup);

    public ExternalLoginFlow? Get(string code)
    {
        Sweep();
        if (!_entries.TryGetValue(code, out var entry) || timeProvider.GetUtcNow() >= entry.ExpiresUtc)
        {
            _entries.TryRemove(code, out _);
            return null;
        }

        return new ExternalLoginFlow(code, entry.UserId, entry.LoginProvider, entry.PendingSignup);
    }

    public void Remove(string code) => _entries.TryRemove(code, out _);

    private string Create(string? userId, string? loginProvider, ExternalSignup? signup)
    {
        var code = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        _entries[code] = new Entry(userId, loginProvider, signup, timeProvider.GetUtcNow().Add(Lifetime));
        return code;
    }

    private void Sweep()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (code, entry) in _entries)
        {
            if (now >= entry.ExpiresUtc)
            {
                _entries.TryRemove(code, out _);
            }
        }
    }

    private sealed record Entry(string? UserId, string? LoginProvider, ExternalSignup? PendingSignup, DateTimeOffset ExpiresUtc);
}
