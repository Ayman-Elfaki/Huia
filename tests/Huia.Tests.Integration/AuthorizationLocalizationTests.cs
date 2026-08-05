using System.Net;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Huia.Tests.Integration;


/// <summary>
/// Covers <c>AuthorizationEndpoints.ApplyUiLocales</c>: an unauthenticated <c>/connect/authorize</c> request
/// carrying the OpenID Connect <c>ui_locales</c> parameter should set Huia's request-localization culture
/// cookie, so the Login/Register pages the caller gets redirected to render in that language instead of
/// falling back to the browser's Accept-Language header. Companion to <see cref="IdentityUiSecurityTests"/>
/// (which covers the Identity UI's own security properties) and <see cref="TokenSecurityTests"/> (the OAuth
/// surface) — this is the one place the authorize endpoint's redirect-to-login behavior itself is exercised
/// anonymously, without first signing a user in.
/// </summary>
public class AuthorizationLocalizationTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string ClientId = "scalar";

    [Fact]
    public async Task Authorize_WhileUnauthenticated_WithASupportedUiLocale_SetsTheCultureCookie()
    {
        using var response = await AuthorizeAsync(uiLocales: "ar");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var cultureCookie = GetCultureCookie(response);
        Assert.NotNull(cultureCookie);
        Assert.Contains("c%3Dar", cultureCookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// OIDC's <c>ui_locales</c> is a space-separated, preference-ordered list (RFC-style language
    /// negotiation) — the first tag Huia actually supports should win, not simply the first tag listed.
    /// </summary>
    [Fact]
    public async Task Authorize_WhileUnauthenticated_WithAnUnsupportedLocaleAheadOfASupportedOne_UsesTheSupportedOne()
    {
        using var response = await AuthorizeAsync(uiLocales: "fr ar");

        var cultureCookie = GetCultureCookie(response);
        Assert.NotNull(cultureCookie);
        Assert.Contains("c%3Dar", cultureCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authorize_WhileUnauthenticated_WithNoSupportedUiLocale_DoesNotSetTheCultureCookie()
    {
        using var response = await AuthorizeAsync(uiLocales: "fr");

        Assert.Null(GetCultureCookie(response));
    }

    [Fact]
    public async Task Authorize_WhileUnauthenticated_WithNoUiLocalesAtAll_DoesNotSetTheCultureCookie()
    {
        using var response = await AuthorizeAsync(uiLocales: null);

        Assert.Null(GetCultureCookie(response));
    }

    private async Task<HttpResponseMessage> AuthorizeAsync(string? uiLocales)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var scope = factory.Services.CreateScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applicationManager.FindByClientIdAsync(ClientId)
            ?? throw new InvalidOperationException($"The '{ClientId}' sample client is not registered.");
        var redirectUri = (await applicationManager.GetRedirectUrisAsync(application)).First();

        var parameters = new List<string>
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
        };
        if (uiLocales is not null)
        {
            parameters.Add($"ui_locales={Uri.EscapeDataString(uiLocales)}");
        }

        return await client.GetAsync($"/connect/authorize?{string.Join('&', parameters)}");
    }

    private static string? GetCultureCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(CookieRequestCultureProvider.DefaultCookieName, StringComparison.Ordinal))
            : null;
}
