using Huia.Common;
using Microsoft.AspNetCore.Http;

namespace Huia.Tests.Unit.Common;

/// <summary>
/// Covers the paths through <see cref="ReturnUrlValidator.ResolveAsync"/> that resolve without ever
/// consulting <c>IOpenIddictApplicationManager</c> (hence the <see langword="null"/>! passed for it below —
/// never dereferenced on these paths). The cross-origin case that does consult it (a registered client's own
/// HomeUri/redirect_uri origin, e.g. <c>ConfirmEmail.cshtml.cs</c>'s "Sign in" link) needs a real
/// OpenIddict-backed store to mean anything, so it's covered by <c>Huia.Tests.Integration.IdentityUiSecurityTests</c>
/// and <c>ErrorPageReturnHomeTests</c> instead, against real registered applications.
/// </summary>
public class ReturnUrlValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Resolve_WithNoReturnUrl_ReturnsRoot(string? returnUrl) =>
        Assert.Equal("/", await ReturnUrlValidator.ResolveAsync(CreateRequest(), returnUrl, null!));

    [Fact]
    public async Task Resolve_WithRootedPath_ReturnsItUnchanged() =>
        Assert.Equal("/account/profile", await ReturnUrlValidator.ResolveAsync(CreateRequest(), "/account/profile", null!));

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    public async Task Resolve_WithProtocolRelativeTrick_FallsBackToRoot(string returnUrl) =>
        Assert.Equal("/", await ReturnUrlValidator.ResolveAsync(CreateRequest(), returnUrl, null!));

    [Fact]
    public async Task Resolve_WithTildeSlashPath_StripsTilde() =>
        Assert.Equal("/account/profile", await ReturnUrlValidator.ResolveAsync(CreateRequest(), "~/account/profile", null!));

    [Fact]
    public async Task Resolve_WithAbsoluteUrlToSameOrigin_ReturnsItUnchanged()
    {
        const string returnUrl = "https://app.example/connect/authorize?client_id=x";

        Assert.Equal(returnUrl, await ReturnUrlValidator.ResolveAsync(CreateRequest(), returnUrl, null!));
    }

    [Fact]
    public async Task Resolve_WithMalformedAbsoluteUrl_FallsBackToRoot() =>
        Assert.Equal("/", await ReturnUrlValidator.ResolveAsync(CreateRequest(), "http://", null!));

    private static HttpRequest CreateRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example");
        return context.Request;
    }
}
