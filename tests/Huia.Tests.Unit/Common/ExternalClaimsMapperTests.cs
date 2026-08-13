using System.Security.Claims;
using Huia.Common;

namespace Huia.Tests.Unit.Common;

public class ExternalClaimsMapperTests
{
    [Fact]
    public void GetEmail_PrefersClaimTypesEmail_OverRawOidcClaim()
    {
        var principal = CreatePrincipal((ClaimTypes.Email, "claimtypes@example.com"), ("email", "raw@example.com"));

        Assert.Equal("claimtypes@example.com", ExternalClaimsMapper.GetEmail(principal));
    }

    [Fact]
    public void GetEmail_FallsBackToRawOidcClaim_WhenClaimTypesEmailMissing()
    {
        var principal = CreatePrincipal(("email", "raw@example.com"));

        Assert.Equal("raw@example.com", ExternalClaimsMapper.GetEmail(principal));
    }

    [Fact]
    public void GetEmail_ReturnsNull_WhenNeitherClaimPresent()
    {
        var principal = CreatePrincipal();

        Assert.Null(ExternalClaimsMapper.GetEmail(principal));
    }

    [Fact]
    public void GetFirstName_PrefersClaimTypesGivenName_OverRawOidcClaim()
    {
        var principal = CreatePrincipal((ClaimTypes.GivenName, "Ada"), ("given_name", "NotAda"));

        Assert.Equal("Ada", ExternalClaimsMapper.GetFirstName(principal));
    }

    [Fact]
    public void GetFirstName_FallsBackToRawOidcClaim_WhenClaimTypesGivenNameMissing()
    {
        var principal = CreatePrincipal(("given_name", "Ada"));

        Assert.Equal("Ada", ExternalClaimsMapper.GetFirstName(principal));
    }

    [Fact]
    public void GetFirstName_ReturnsNull_WhenNeitherClaimPresent()
    {
        var principal = CreatePrincipal();

        Assert.Null(ExternalClaimsMapper.GetFirstName(principal));
    }

    [Fact]
    public void GetLastName_PrefersClaimTypesSurname_OverRawOidcClaim()
    {
        var principal = CreatePrincipal((ClaimTypes.Surname, "Lovelace"), ("family_name", "NotLovelace"));

        Assert.Equal("Lovelace", ExternalClaimsMapper.GetLastName(principal));
    }

    [Fact]
    public void GetLastName_FallsBackToRawOidcClaim_WhenClaimTypesSurnameMissing()
    {
        var principal = CreatePrincipal(("family_name", "Lovelace"));

        Assert.Equal("Lovelace", ExternalClaimsMapper.GetLastName(principal));
    }

    [Fact]
    public void GetLastName_ReturnsNull_WhenNeitherClaimPresent()
    {
        var principal = CreatePrincipal();

        Assert.Null(ExternalClaimsMapper.GetLastName(principal));
    }

    [Fact]
    public void GetPicture_ReturnsRawOidcClaim_WhenPresent()
    {
        var principal = CreatePrincipal(("picture", "https://example.com/avatar.png"));

        Assert.Equal("https://example.com/avatar.png", ExternalClaimsMapper.GetPicture(principal));
    }

    [Fact]
    public void GetPicture_ReturnsNull_WhenAbsent()
    {
        var principal = CreatePrincipal();

        Assert.Null(ExternalClaimsMapper.GetPicture(principal));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("not-a-bool", false)]
    public void IsEmailVerified_ReadsRawOidcClaim_CaseInsensitively(string claimValue, bool expected)
    {
        var principal = CreatePrincipal(("email_verified", claimValue));

        Assert.Equal(expected, ExternalClaimsMapper.IsEmailVerified(principal));
    }

    [Fact]
    public void IsEmailVerified_ReturnsFalse_WhenClaimAbsent()
    {
        var principal = CreatePrincipal();

        Assert.False(ExternalClaimsMapper.IsEmailVerified(principal));
    }

    private static ClaimsPrincipal CreatePrincipal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "External");
        return new ClaimsPrincipal(identity);
    }
}
