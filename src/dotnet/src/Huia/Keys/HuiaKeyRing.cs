using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Huia.Keys;

/// <summary>
/// Default <see cref="IHuiaKeyRing"/>. Loads the small key rows for a tenant through <c>HybridCache</c>
/// and turns them into crypto objects on the way out, memoizing the expensive RSA import by <c>kid</c>
/// (a key is immutable once created).
/// </summary>
internal sealed class HuiaKeyRing(
    HybridCache cache,
    IServiceScopeFactory scopeFactory,
    IHuiaKeyProtector protector) : IHuiaKeyRing
{
    private static readonly ConcurrentDictionary<string, HuiaSigningKeyMaterial> MaterialByKeyId = new(StringComparer.Ordinal);
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(5),
    };

    public async ValueTask<HuiaSigningKeyMaterial> GetActiveSigningKeyAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(tenantId, cancellationToken);
        var active = snapshot.Keys.FirstOrDefault(k => k.Status == nameof(HuiaSigningKeyStatus.Active))
                     ?? snapshot.Keys.FirstOrDefault(k => k.Status == nameof(HuiaSigningKeyStatus.Pending));

        if (active is null)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' has no active signing key. The key bootstrapper should have created one at start-up.");
        }

        return Materialize(active);
    }

    public async ValueTask<IReadOnlyList<HuiaSigningKeyMaterial>> GetPublishedKeysAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(tenantId, cancellationToken);
        return [.. snapshot.Keys.Select(Materialize)];
    }

    public async ValueTask<IReadOnlyList<JsonWebKey>> GetPublishedJwksAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var keys = await GetPublishedKeysAsync(tenantId, cancellationToken);
        return [.. keys.Select(k => k.PublicJwk)];
    }

    public ValueTask InvalidateAsync(string tenantId) => cache.RemoveAsync(CacheKey(tenantId));

    private async ValueTask<KeyRingSnapshot> GetSnapshotAsync(string tenantId, CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(
            CacheKey(tenantId),
            (scopeFactory, tenantId),
            static async (state, ct) =>
            {
                await using var scope = state.scopeFactory.CreateAsyncScope();
                var keyStore = scope.ServiceProvider.GetRequiredService<IHuiaSigningKeyStore>();

                var rows = await keyStore.GetPublishedKeysAsync(state.tenantId, ct);
                var records = rows
                    .Select(k => new KeyRecord(
                        k.KeyId,
                        k.Status.ToString(),
                        k.PublicJwkJson,
                        k.Status is HuiaSigningKeyStatus.Active or HuiaSigningKeyStatus.Pending ? k.WrappedPrivateKey : null))
                    .ToList();

                return new KeyRingSnapshot(records);
            },
            CacheOptions,
            cancellationToken: cancellationToken);
    }

    private HuiaSigningKeyMaterial Materialize(KeyRecord record)
    {
        return MaterialByKeyId.GetOrAdd(record.KeyId, static (_, arg) => Build(arg.record, arg.protector), (record, protector));
    }

    private static HuiaSigningKeyMaterial Build(KeyRecord record, IHuiaKeyProtector protector)
    {
        var publicJwk = JsonWebKey.Create(record.PublicJwkJson);
        SecurityKey securityKey;
        SigningCredentials? signingCredentials = null;

        if (!string.IsNullOrEmpty(record.WrappedPrivateKey))
        {
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(protector.Unprotect(record.WrappedPrivateKey), out _);
            var rsaKey = new RsaSecurityKey(rsa) { KeyId = record.KeyId };
            securityKey = rsaKey;
            signingCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);
        }
        else
        {
            securityKey = publicJwk;
        }

        return new HuiaSigningKeyMaterial
        {
            KeyId = record.KeyId,
            Status = Enum.Parse<HuiaSigningKeyStatus>(record.Status),
            PublicJwk = publicJwk,
            SecurityKey = securityKey,
            SigningCredentials = signingCredentials,
        };
    }

    private static string CacheKey(string tenantId) => $"huia:keys:{tenantId}";

    private sealed record KeyRingSnapshot(List<KeyRecord> Keys);

    private sealed record KeyRecord(string KeyId, string Status, string PublicJwkJson, string? WrappedPrivateKey);
}
