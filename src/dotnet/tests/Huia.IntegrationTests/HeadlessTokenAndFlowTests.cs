using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Huia.Headless.EntityFrameworkCore;
using Huia.Headless.EntityFrameworkCore.Stores;
using Huia.Headless.Options;
using Huia.Headless.Services;
using Huia.Identity;
using Huia.IntegrationTests.Infrastructure;
using Huia.Keys;
using Huia.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

namespace Huia.IntegrationTests;

public sealed class HeadlessTokenAndFlowTests
{
    private static async Task<HuiaHeadlessDbContext> CreateInMemoryDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<HuiaHeadlessDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new HuiaHeadlessDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task IssueAsync_creates_active_token_record_with_hashed_storage()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateInMemoryDbContextAsync(connection);

        var timeProvider = new FakeTimeProvider();
        var store = new EfHuiaHeadlessRefreshTokenStore<HuiaHeadlessDbContext>(db, timeProvider);

        var (rawToken, record) = await store.IssueAsync("tenant-1", "user-1", TimeSpan.FromDays(30));

        rawToken.ShouldNotBeNullOrWhiteSpace();
        record.TenantId.ShouldBe("tenant-1");
        record.UserId.ShouldBe("user-1");
        record.FamilyId.ShouldNotBeNullOrWhiteSpace();
        record.TokenHash.ShouldNotBe(rawToken); // Stored hashed
        record.IsActive(timeProvider.GetUtcNow()).ShouldBeTrue();

        var inDb = await db.RefreshTokens.SingleOrDefaultAsync(t => t.Id == record.Id);
        inDb.ShouldNotBeNull();
        inDb.TenantId.ShouldBe("tenant-1");
        inDb.UserId.ShouldBe("user-1");
        inDb.TokenHash.ShouldBe(record.TokenHash);
    }

    [Fact]
    public async Task RotateAsync_rotates_token_and_maintains_family_id()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateInMemoryDbContextAsync(connection);

        var timeProvider = new FakeTimeProvider();
        var store = new EfHuiaHeadlessRefreshTokenStore<HuiaHeadlessDbContext>(db, timeProvider);

        var (rawToken1, record1) = await store.IssueAsync("tenant-1", "user-1", TimeSpan.FromDays(30));

        timeProvider.Advance(TimeSpan.FromHours(1));

        var rotation = await store.RotateAsync("tenant-1", rawToken1, TimeSpan.FromDays(30), slidingExpiration: true);

        rotation.Status.ShouldBe(RefreshTokenRotationStatus.Success);
        rotation.NewRawRefreshToken.ShouldNotBeNullOrWhiteSpace();
        rotation.NewRawRefreshToken.ShouldNotBe(rawToken1);
        rotation.UserId.ShouldBe("user-1");

        // Old token is revoked and marked replaced
        var oldInDb = await db.RefreshTokens.SingleAsync(t => t.Id == record1.Id);
        oldInDb.RevokedAt.ShouldNotBeNull();
        oldInDb.ReplacedByTokenHash.ShouldNotBeNull();

        // New token is active and has the same family id
        var tokensInFamily = await db.RefreshTokens.Where(t => t.FamilyId == record1.FamilyId).ToListAsync();
        tokensInFamily.Count.ShouldBe(2);

        var newToken = tokensInFamily.Single(t => t.RevokedAt == null);
        newToken.TokenHash.ShouldBe(oldInDb.ReplacedByTokenHash);
    }

    [Fact]
    public async Task RotateAsync_detects_token_reuse_and_revokes_entire_family()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateInMemoryDbContextAsync(connection);

        var timeProvider = new FakeTimeProvider();
        var store = new EfHuiaHeadlessRefreshTokenStore<HuiaHeadlessDbContext>(db, timeProvider);

        // Step 1: Issue token 1
        var (rawToken1, record1) = await store.IssueAsync("tenant-1", "user-1", TimeSpan.FromDays(30));

        // Step 2: Rotate token 1 -> token 2
        var rotate1 = await store.RotateAsync("tenant-1", rawToken1, TimeSpan.FromDays(30), slidingExpiration: true);
        rotate1.Status.ShouldBe(RefreshTokenRotationStatus.Success);
        var rawToken2 = rotate1.NewRawRefreshToken!;

        // Step 3: Attacker/client attempts to reuse old rawToken1!
        var rotateReuse = await store.RotateAsync("tenant-1", rawToken1, TimeSpan.FromDays(30), slidingExpiration: true);
        rotateReuse.Status.ShouldBe(RefreshTokenRotationStatus.Revoked);

        // Step 4: Verify entire family is now revoked, including token 2
        var familyTokens = await db.RefreshTokens.Where(t => t.FamilyId == record1.FamilyId).ToListAsync();
        familyTokens.Count.ShouldBe(2);
        familyTokens.All(t => t.RevokedAt != null).ShouldBeTrue();

        // Attempting to rotate token 2 also fails because its family was revoked
        var rotateToken2 = await store.RotateAsync("tenant-1", rawToken2, TimeSpan.FromDays(30), slidingExpiration: true);
        rotateToken2.Status.ShouldBe(RefreshTokenRotationStatus.Revoked);
    }

    [Fact]
    public async Task RevokeFamilyAsync_revokes_all_tokens_in_family()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateInMemoryDbContextAsync(connection);

        var timeProvider = new FakeTimeProvider();
        var store = new EfHuiaHeadlessRefreshTokenStore<HuiaHeadlessDbContext>(db, timeProvider);

        var (rawToken, record) = await store.IssueAsync("tenant-1", "user-1", TimeSpan.FromDays(30));
        var rotate = await store.RotateAsync("tenant-1", rawToken, TimeSpan.FromDays(30), slidingExpiration: true);
        rotate.Status.ShouldBe(RefreshTokenRotationStatus.Success);

        await store.RevokeFamilyAsync("tenant-1", rotate.NewRawRefreshToken!);

        var tokens = await db.RefreshTokens.Where(t => t.FamilyId == record.FamilyId).ToListAsync();
        tokens.All(t => t.RevokedAt != null).ShouldBeTrue();
    }

    [Fact]
    public async Task RevokeUserSessionsAsync_revokes_all_active_tokens_for_user()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateInMemoryDbContextAsync(connection);

        var timeProvider = new FakeTimeProvider();
        var store = new EfHuiaHeadlessRefreshTokenStore<HuiaHeadlessDbContext>(db, timeProvider);

        var (token1, _) = await store.IssueAsync("tenant-1", "user-1", TimeSpan.FromDays(30));
        var (token2, _) = await store.IssueAsync("tenant-1", "user-1", TimeSpan.FromDays(30));
        var (tokenOther, _) = await store.IssueAsync("tenant-1", "user-2", TimeSpan.FromDays(30));

        await store.RevokeUserSessionsAsync("tenant-1", "user-1");

        var user1Tokens = await db.RefreshTokens.Where(t => t.UserId == "user-1").ToListAsync();
        user1Tokens.All(t => t.RevokedAt != null).ShouldBeTrue();

        var user2Tokens = await db.RefreshTokens.Where(t => t.UserId == "user-2").ToListAsync();
        user2Tokens.All(t => t.RevokedAt == null).ShouldBeTrue();
    }

    [Fact]
    public void Provisional_token_protects_and_unprotects_payload()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        using var sp = services.BuildServiceProvider();
        var dp = sp.GetRequiredService<IDataProtectionProvider>();

        var timeProvider = new FakeTimeProvider();
        var tokenService = new HuiaHeadlessTokenService(
            null!, new HuiaOptions(), null!, null!, dp, timeProvider);

        var token = tokenService.CreateProvisionalToken("tenant-1", "user-123");
        token.ShouldNotBeNullOrWhiteSpace();

        var parsed = tokenService.ValidateProvisionalToken(token);
        parsed.ShouldNotBeNull();
        parsed.Value.TenantId.ShouldBe("tenant-1");
        parsed.Value.UserId.ShouldBe("user-123");

        // Expired token returns null
        timeProvider.Advance(TimeSpan.FromMinutes(16));
        tokenService.ValidateProvisionalToken(token).ShouldBeNull();
    }
}
