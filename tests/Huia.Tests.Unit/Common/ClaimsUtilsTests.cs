using System.Security.Claims;
using Huia.Common;
using Huia.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.Tests.Unit.Common;

public class ClaimsUtilsTests
{
    [Theory]
    [InlineData(Claims.Name)]
    [InlineData(Claims.PreferredUsername)]
    [InlineData(Claims.GivenName)]
    [InlineData(Claims.FamilyName)]
    public void GetDestinations_ProfileClaim_GoesToBothTokens_WhenProfileScopeGranted(string claimType)
    {
        var identity = CreateIdentity(OpenIddictConstants.Scopes.Profile);

        var destinations = ClaimsUtils.GetDestinations(new Claim(claimType, "value"), identity);

        Assert.Equal([Destinations.AccessToken, Destinations.IdentityToken], destinations, StringComparer.Ordinal);
    }

    [Fact]
    public void GetDestinations_NameClaim_AccessTokenOnly_WhenProfileScopeNotGranted()
    {
        var identity = CreateIdentity();

        var destinations = ClaimsUtils.GetDestinations(new Claim(Claims.Name, "value"), identity);

        Assert.Equal([Destinations.AccessToken], destinations, StringComparer.Ordinal);
    }

    [Fact]
    public void GetDestinations_EmailClaim_GoesToBothTokens_WhenEmailScopeGranted()
    {
        var identity = CreateIdentity(OpenIddictConstants.Scopes.Email);

        var destinations = ClaimsUtils.GetDestinations(new Claim(Claims.Email, "user@example.com"), identity);

        Assert.Equal([Destinations.AccessToken, Destinations.IdentityToken], destinations, StringComparer.Ordinal);
    }

    [Fact]
    public void GetDestinations_RoleClaim_GoesToBothTokens_WhenRolesScopeGranted()
    {
        var identity = CreateIdentity(OpenIddictConstants.Scopes.Roles);

        var destinations = ClaimsUtils.GetDestinations(new Claim(Claims.Role, "admin"), identity);

        Assert.Equal([Destinations.AccessToken, Destinations.IdentityToken], destinations, StringComparer.Ordinal);
    }

    [Fact]
    public void GetDestinations_SecurityStamp_NeverIncluded()
    {
        var identity = CreateIdentity(OpenIddictConstants.Scopes.Profile, OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Roles);

        var destinations = ClaimsUtils.GetDestinations(new Claim(HuiaSignInManager.SecurityStampClaimType, "stamp"), identity);

        Assert.Empty(destinations);
    }

    [Fact]
    public void GetDestinations_UnknownClaim_AccessTokenOnly()
    {
        var identity = CreateIdentity(OpenIddictConstants.Scopes.Profile, OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Roles);

        var destinations = ClaimsUtils.GetDestinations(new Claim("custom_claim", "value"), identity);

        Assert.Equal([Destinations.AccessToken], destinations, StringComparer.Ordinal);
    }

    private static ClaimsIdentity CreateIdentity(params string[] scopes)
    {
        var identity = new ClaimsIdentity(authenticationType: "Huia", nameType: Claims.Name, roleType: Claims.Role);
        identity.SetScopes(scopes);
        return identity;
    }
}