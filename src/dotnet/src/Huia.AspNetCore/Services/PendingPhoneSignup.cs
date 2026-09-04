using System.Collections.Concurrent;
using Huia.Options;

namespace Huia.AspNetCore.Services;

/// <summary>A number that has requested a code but has no account yet.</summary>
/// <param name="Id">Opaque identifier carried in the flow token.</param>
/// <param name="TenantId">The tenant the sign-in is running in.</param>
/// <param name="PhoneNumber">The E.164 number.</param>
public sealed record PendingPhoneSignupRecord(string Id, string TenantId, string PhoneNumber);

/// <summary>
/// Holds hashed one-time codes for numbers with no account, so auto-provisioning never writes a
/// blank-name <see cref="EntityFrameworkCore.Entities.HuiaUser"/> at request time. The default
/// implementation is in-process only; a multi-node deployment replaces it with a distributed store.
/// </summary>
public interface IPendingPhoneSignup
{
    /// <summary>Creates a pending record for a number and returns its id.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="phoneNumber">The E.164 number.</param>
    /// <param name="code">The plain code (hashed before storage).</param>
    /// <param name="options">The tenant's phone-login options.</param>
    /// <returns>The new record id.</returns>
    string Create(string tenantId, string phoneNumber, string code, PhoneOptions options);

    /// <summary>Re-issues a code against an existing record in place.</summary>
    /// <param name="id">The record id.</param>
    /// <param name="code">The new plain code.</param>
    /// <param name="options">The tenant's phone-login options.</param>
    /// <returns><see langword="true"/> when the record existed and was updated.</returns>
    bool Reissue(string id, string code, PhoneOptions options);

    /// <summary>Gets a record by id, or <see langword="null"/> when unknown or expired.</summary>
    /// <param name="id">The record id.</param>
    /// <returns>The record or <see langword="null"/>.</returns>
    PendingPhoneSignupRecord? Get(string id);

    /// <summary>Verifies a code against a record, consuming it on success.</summary>
    /// <param name="id">The record id.</param>
    /// <param name="code">The candidate code.</param>
    /// <param name="options">The tenant's phone-login options.</param>
    /// <returns>The verification outcome.</returns>
    OtpVerifyResult Verify(string id, string code, PhoneOptions options);

    /// <summary>Drops a record.</summary>
    /// <param name="id">The record id.</param>
    void Remove(string id);
}

/// <summary>In-memory <see cref="IPendingPhoneSignup"/> with lazy expiry.</summary>
internal sealed class PendingPhoneSignup(TimeProvider timeProvider) : IPendingPhoneSignup
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string Create(string tenantId, string phoneNumber, string code, PhoneOptions options)
    {
        Sweep();
        var id = Guid.NewGuid().ToString("N");
        var (hash, salt) = OtpHashing.Create(code);
        _entries[id] = new Entry(tenantId, phoneNumber, hash, salt,
            timeProvider.GetUtcNow().Add(options.CodeLifetime),
            timeProvider.GetUtcNow().Add(options.PendingSignupLifetime), 0);
        return id;
    }

    public bool Reissue(string id, string code, PhoneOptions options)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return false;
        }

        var (hash, salt) = OtpHashing.Create(code);
        _entries[id] = entry with
        {
            Hash = hash,
            Salt = salt,
            CodeExpiresUtc = timeProvider.GetUtcNow().Add(options.CodeLifetime),
            Attempts = 0,
        };
        return true;
    }

    public PendingPhoneSignupRecord? Get(string id) =>
        TryGetLive(id, out var entry) ? new PendingPhoneSignupRecord(id, entry.TenantId, entry.PhoneNumber) : null;

    public OtpVerifyResult Verify(string id, string code, PhoneOptions options)
    {
        if (!TryGetLive(id, out var entry))
        {
            return OtpVerifyResult.NotFound;
        }

        if (timeProvider.GetUtcNow() >= entry.CodeExpiresUtc)
        {
            _entries.TryRemove(id, out _);
            return OtpVerifyResult.Expired;
        }

        if (OtpHashing.Verify(code, entry.Hash, entry.Salt))
        {
            return OtpVerifyResult.Success;
        }

        var attempts = entry.Attempts + 1;
        if (attempts >= options.MaxVerificationAttempts)
        {
            _entries.TryRemove(id, out _);
            return OtpVerifyResult.TooManyAttempts;
        }

        _entries[id] = entry with { Attempts = attempts };
        return OtpVerifyResult.Invalid;
    }

    public void Remove(string id) => _entries.TryRemove(id, out _);

    private bool TryGetLive(string id, out Entry entry)
    {
        if (_entries.TryGetValue(id, out entry!) && timeProvider.GetUtcNow() < entry.RecordExpiresUtc)
        {
            return true;
        }

        _entries.TryRemove(id, out _);
        entry = default!;
        return false;
    }

    private void Sweep()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (id, entry) in _entries)
        {
            if (now >= entry.RecordExpiresUtc)
            {
                _entries.TryRemove(id, out _);
            }
        }
    }

    private sealed record Entry(
        string TenantId,
        string PhoneNumber,
        string Hash,
        string Salt,
        DateTimeOffset CodeExpiresUtc,
        DateTimeOffset RecordExpiresUtc,
        int Attempts);
}
