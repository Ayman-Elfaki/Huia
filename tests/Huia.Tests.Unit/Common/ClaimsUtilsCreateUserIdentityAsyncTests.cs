using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.Tests.Unit.Common;

/// <summary>
/// Covers <see cref="ClaimsUtils.CreateUserIdentityAsync"/> — the claim-building half of <see cref="ClaimsUtils"/>
/// (the destination-routing half is covered by <see cref="ClaimsUtilsTests"/>). Backed by
/// <see cref="FakeUserStore"/> so a real <see cref="UserManager{HuiaUser}"/> can be exercised without a database.
/// </summary>
public class ClaimsUtilsCreateUserIdentityAsyncTests
{
    [Fact]
    public async Task CreateUserIdentityAsync_IncludesCoreProfileClaims()
    {
        var user = new HuiaUser
        {
            Id = "user-1",
            UserName = "alice",
            Email = "alice@example.com",
            FirstName = "Alice",
            LastName = "Doe",
            Picture = "https://example.com/alice.png",
        };
        var userManager = CreateUserManager(user, roles: ["admin"]);

        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, scopes: ["openid", "profile"]);

        Assert.Equal("user-1", identity.FindFirst(Claims.Subject)?.Value);
        Assert.Equal("alice@example.com", identity.FindFirst(Claims.Email)?.Value);
        Assert.Equal("alice", identity.FindFirst(Claims.Name)?.Value);
        Assert.Equal("alice", identity.FindFirst(Claims.PreferredUsername)?.Value);
        Assert.Equal("Alice", identity.FindFirst(Claims.GivenName)?.Value);
        Assert.Equal("Doe", identity.FindFirst(Claims.FamilyName)?.Value);
        Assert.Equal("https://example.com/alice.png", identity.FindFirst(Claims.Picture)?.Value);
        Assert.Equal("admin", identity.FindFirst(Claims.Role)?.Value);
    }

    [Fact]
    public async Task CreateUserIdentityAsync_NoRoles_AddsNoRoleClaim()
    {
        var user = new HuiaUser { Id = "user-1", UserName = "alice" };
        var userManager = CreateUserManager(user, roles: []);

        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, scopes: []);

        Assert.Null(identity.FindFirst(Claims.Role));
    }

    [Fact]
    public async Task CreateUserIdentityAsync_MultipleRoles_AddsOneClaimPerRole()
    {
        var user = new HuiaUser { Id = "user-1", UserName = "alice" };
        var userManager = CreateUserManager(user, roles: ["admin", "editor"]);

        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, scopes: []);

        Assert.Equal(["admin", "editor"], identity.FindAll(Claims.Role).Select(c => c.Value), StringComparer.Ordinal);
    }

    [Fact]
    public async Task CreateUserIdentityAsync_SetsRequestedScopes()
    {
        var user = new HuiaUser { Id = "user-1", UserName = "alice" };
        var userManager = CreateUserManager(user, roles: []);

        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, scopes: ["openid", "profile"]);

        Assert.Equal(["openid", "profile"], identity.GetScopes(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task CreateUserIdentityAsync_NameClaim_GoesToBothTokens_WhenProfileScopeGranted()
    {
        var user = new HuiaUser { Id = "user-1", UserName = "alice" };
        var userManager = CreateUserManager(user, roles: []);

        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, scopes: [OpenIddictConstants.Scopes.Profile]);

        var nameClaim = Assert.Single(identity.FindAll(Claims.Name));
        Assert.Equal([Destinations.AccessToken, Destinations.IdentityToken], nameClaim.GetDestinations(),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task CreateUserIdentityAsync_NameClaim_AccessTokenOnly_WhenProfileScopeNotGranted()
    {
        var user = new HuiaUser { Id = "user-1", UserName = "alice" };
        var userManager = CreateUserManager(user, roles: []);

        var identity = await ClaimsUtils.CreateUserIdentityAsync(userManager, user, scopes: []);

        var nameClaim = Assert.Single(identity.FindAll(Claims.Name));
        Assert.Equal([Destinations.AccessToken], nameClaim.GetDestinations(), StringComparer.Ordinal);
    }

    private static UserManager<HuiaUser> CreateUserManager(HuiaUser user, IReadOnlyList<string> roles) => new(
        new FakeUserStore(user, roles),
        Options.Create(new IdentityOptions()),
        new PasswordHasher<HuiaUser>(),
        [],
        [],
        new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(),
        new ServiceCollection().BuildServiceProvider(),
        NullLogger<UserManager<HuiaUser>>.Instance);
}
