using System.Collections.Concurrent;

namespace Huia.Headless.Services;

/// <summary>
/// Correlates a headless phone-login <c>start</c> call with its later <c>verify</c>/<c>complete-profile</c>
/// call. <c>Huia.OpenId</c>'s Razor pages carry the equivalent state through an encrypted flow token
/// round-tripped via a hidden form field across page loads; a stateless JSON API has no such round trip,
/// so this in-process store plays the same role — the opaque, unguessable <see cref="PhoneLoginFlow.Id"/>
/// is the client's only handle, exactly as a session id would be.
/// </summary>
public sealed record PhoneLoginFlow(
    string Id,
    string PhoneNumber,
    string? UserId,
    string? PendingSignupId,
    bool Verified);

/// <summary>See <see cref="PhoneLoginFlow"/>.</summary>
public interface IPhoneLoginFlowStore
{
    /// <summary>Starts a flow for a number, tied to either an existing account, a pending signup, or neither.</summary>
    /// <param name="phoneNumber">The E.164 number.</param>
    /// <param name="userId">The existing account id, when the number matched one.</param>
    /// <param name="pendingSignupId">The pending-signup id, when the number is new and auto-provisioning is on.</param>
    /// <returns>The new flow id.</returns>
    string Create(string phoneNumber, string? userId, string? pendingSignupId);

    /// <summary>Gets a flow by id, or <see langword="null"/> when unknown or expired.</summary>
    /// <param name="id">The flow id.</param>
    /// <returns>The flow or <see langword="null"/>.</returns>
    PhoneLoginFlow? Get(string id);

    /// <summary>
    /// Marks a flow verified in place (the code has been checked) — used when the matching account or
    /// pending signup still needs a first/last name before it can sign in.
    /// </summary>
    /// <param name="id">The flow id.</param>
    void MarkVerified(string id);

    /// <summary>Drops a flow.</summary>
    /// <param name="id">The flow id.</param>
    void Remove(string id);
}

/// <summary>In-memory <see cref="IPhoneLoginFlowStore"/> with lazy expiry.</summary>
internal sealed class PhoneLoginFlowStore(TimeProvider timeProvider) : IPhoneLoginFlowStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string Create(string phoneNumber, string? userId, string? pendingSignupId)
    {
        Sweep();
        var id = Guid.NewGuid().ToString("N");
        _entries[id] = new Entry(phoneNumber, userId, pendingSignupId, Verified: false, timeProvider.GetUtcNow().Add(Lifetime));
        return id;
    }

    public PhoneLoginFlow? Get(string id)
    {
        if (!_entries.TryGetValue(id, out var entry) || timeProvider.GetUtcNow() >= entry.ExpiresUtc)
        {
            _entries.TryRemove(id, out _);
            return null;
        }

        return new PhoneLoginFlow(id, entry.PhoneNumber, entry.UserId, entry.PendingSignupId, entry.Verified);
    }

    public void MarkVerified(string id)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            _entries[id] = entry with { Verified = true };
        }
    }

    public void Remove(string id) => _entries.TryRemove(id, out _);

    private void Sweep()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (id, entry) in _entries)
        {
            if (now >= entry.ExpiresUtc)
            {
                _entries.TryRemove(id, out _);
            }
        }
    }

    private sealed record Entry(
        string PhoneNumber,
        string? UserId,
        string? PendingSignupId,
        bool Verified,
        DateTimeOffset ExpiresUtc);
}
