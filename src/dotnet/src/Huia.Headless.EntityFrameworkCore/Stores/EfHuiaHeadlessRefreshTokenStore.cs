using System.Security.Cryptography;
using System.Text;
using Huia.Headless.EntityFrameworkCore.Entities;
using Huia.Headless.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Huia.Headless.EntityFrameworkCore.Stores;

/// <summary>
/// Entity Framework Core implementation of <see cref="IHuiaHeadlessRefreshTokenStore"/>.
/// Supports SHA-256 hashed storage, token rotation, and family-wide revocation on token reuse.
/// </summary>
/// <typeparam name="TContext">The concrete <see cref="HuiaHeadlessDbContext"/>.</typeparam>
public class EfHuiaHeadlessRefreshTokenStore<TContext> : IHuiaHeadlessRefreshTokenStore
    where TContext : HuiaHeadlessDbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new store instance.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="timeProvider">Supplies the current time.</param>
    public EfHuiaHeadlessRefreshTokenStore(TContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<(string RawToken, HuiaRefreshTokenRecord Record)> IssueAsync(
        string tenantId, string userId, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var rawToken = GenerateRawToken();
        var tokenHash = HashToken(rawToken);
        var now = _timeProvider.GetUtcNow();
        var familyId = Guid.NewGuid().ToString("N");

        var entity = new HuiaRefreshToken
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            IssuedAt = now,
            ExpiresAt = now.Add(lifetime),
            RevokedAt = null,
            ReplacedByTokenHash = null,
        };

        _context.RefreshTokens.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        var record = new HuiaRefreshTokenRecord
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            UserId = entity.UserId,
            TokenHash = entity.TokenHash,
            FamilyId = entity.FamilyId,
            IssuedAt = entity.IssuedAt,
            ExpiresAt = entity.ExpiresAt,
            RevokedAt = entity.RevokedAt,
            ReplacedByTokenHash = entity.ReplacedByTokenHash,
        };

        return (rawToken, record);
    }

    /// <inheritdoc />
    public async Task<RefreshTokenRotationResult> RotateAsync(
        string tenantId, string rawRefreshToken, TimeSpan lifetime, bool slidingExpiration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var hash = HashToken(rawRefreshToken);
        var existing = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return new RefreshTokenRotationResult(RefreshTokenRotationStatus.NotFound, null, null, null);
        }

        var now = _timeProvider.GetUtcNow();

        // Detect reuse: token already revoked or replaced -> revoke the entire token family
        if (existing.RevokedAt is not null || existing.ReplacedByTokenHash is not null)
        {
            var familyTokens = await _context.RefreshTokens
                .Where(t => t.TenantId == tenantId && t.FamilyId == existing.FamilyId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var t in familyTokens)
            {
                t.RevokedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return new RefreshTokenRotationResult(RefreshTokenRotationStatus.Revoked, null, null, null);
        }

        // Check if token expired
        if (now >= existing.ExpiresAt)
        {
            existing.RevokedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            return new RefreshTokenRotationResult(RefreshTokenRotationStatus.Expired, null, null, null);
        }

        // Rotate: mark current token retired and issue replacement in same family
        var newRawToken = GenerateRawToken();
        var newHash = HashToken(newRawToken);

        existing.RevokedAt = now;
        existing.ReplacedByTokenHash = newHash;

        var newExpiresAt = slidingExpiration ? now.Add(lifetime) : existing.ExpiresAt;
        var newEntity = new HuiaRefreshToken
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            UserId = existing.UserId,
            TokenHash = newHash,
            FamilyId = existing.FamilyId,
            IssuedAt = now,
            ExpiresAt = newExpiresAt,
            RevokedAt = null,
            ReplacedByTokenHash = null,
        };

        _context.RefreshTokens.Add(newEntity);
        await _context.SaveChangesAsync(cancellationToken);

        var remainingLifetime = newExpiresAt - now;
        return new RefreshTokenRotationResult(RefreshTokenRotationStatus.Success, newRawToken, existing.UserId, remainingLifetime);
    }

    /// <inheritdoc />
    public async Task RevokeFamilyAsync(string tenantId, string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var hash = HashToken(rawRefreshToken);
        var existing = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var familyTokens = await _context.RefreshTokens
            .Where(t => t.TenantId == tenantId && t.FamilyId == existing.FamilyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var t in familyTokens)
        {
            t.RevokedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeUserSessionsAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var now = _timeProvider.GetUtcNow();
        var userTokens = await _context.RefreshTokens
            .Where(t => t.TenantId == tenantId && t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var t in userTokens)
        {
            t.RevokedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateRawToken()
    {
        return Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    }
}

/// <summary>Default non-generic EF refresh token store using <see cref="HuiaHeadlessDbContext"/>.</summary>
public sealed class EfHuiaHeadlessRefreshTokenStore : EfHuiaHeadlessRefreshTokenStore<HuiaHeadlessDbContext>
{
    /// <summary>Creates a new store instance.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="timeProvider">Supplies the current time.</param>
    public EfHuiaHeadlessRefreshTokenStore(HuiaHeadlessDbContext context, TimeProvider timeProvider)
        : base(context, timeProvider)
    {
    }
}
