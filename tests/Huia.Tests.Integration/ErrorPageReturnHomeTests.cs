using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Huia.Tests.Integration;

/// <summary>
/// Covers <c>ErrorModel</c>'s "return home" link: OpenIddict's connect endpoints re-execute onto Huia's
/// branded error page (<c>EnableStatusCodePagesIntegration()</c>) for failures like an unregistered <c>redirect_uri</c> — a
/// scenario where <c>client_id</c> itself is still a real, known client, so there's a safe place to send the
/// user "back to the application" even though the specific request failed.
/// </summary>
public class ErrorPageReturnHomeTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    [Fact]
    public async Task Authorize_WithUnregisteredRedirectUri_ForARegisteredClient_ShowsReturnHomeLink()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await AuthorizeWithBadRedirectUriAsync(client, clientId: "todo-web");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Back to home", html, StringComparison.Ordinal);
        Assert.Contains("href=\"http://localhost:3000\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_WithUnregisteredRedirectUri_ForAnUnknownClient_DoesNotShowReturnHomeLink()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await AuthorizeWithBadRedirectUriAsync(client, clientId: "does-not-exist");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Back to home", html, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> AuthorizeWithBadRedirectUriAsync(HttpClient client, string clientId)
    {
        var query = string.Join('&', new[]
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString("https://evil.example/steal")}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
        });

        return client.GetAsync($"/connect/authorize?{query}");
    }
}