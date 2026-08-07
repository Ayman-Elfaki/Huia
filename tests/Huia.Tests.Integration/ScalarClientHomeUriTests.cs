using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Huia.Tests.Integration;

/// <summary>
/// Regression test for the <c>scalar</c> sample client's confirmation-email "Sign in" link 404ing: unlike
/// <c>todo-web</c>, <c>scalar</c>'s registered redirect_uris (<c>/scalar</c>, <c>/scalar/v1</c>) don't share
/// TodoApi's own origin as a page in their own right — that origin's bare root ("/") isn't mapped to anything.
/// Before <c>Huia.TodoApi</c>'s Program.cs set <c>scalar</c>'s <see cref="Huia.Applications.ClientApplicationOptions.HomeUri"/>
/// explicitly, <see cref="Huia.Applications.ApplicationHomeUriResolver"/> fell back to that bare origin, and registering
/// through Scalar's own "Authorize" button, then confirming by email and clicking "Sign in", landed on a 404
/// instead of back on the Scalar reference page.
/// </summary>
public class ScalarClientHomeUriTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string ClientId = "scalar";

    [Fact]
    public async Task Register_ViaScalarClient_ConfirmationLinkReturnUrl_ResolvesToScalarPage_NotBareOrigin()
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

        var query = string.Join('&', new[]
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "scope=todos",
            "code_challenge=abc",
            "code_challenge_method=plain",
        });
        using var authorizeResponse = await client.GetAsync($"/connect/authorize?{query}");
        Assert.Equal(HttpStatusCode.Found, authorizeResponse.StatusCode);
        var loginReturnUrl = ExtractQueryParameter(authorizeResponse.Headers.Location!.ToString(), "ReturnUrl");

        var email = $"scalar-confirm-test-{Guid.NewGuid():N}@example.com";
        using var registerResponse =
            await IdentityUiTestHelpers.RegisterAsync(client, email, "P@ssw0rd123!", loginReturnUrl);
        Assert.Equal(HttpStatusCode.Found, registerResponse.StatusCode);

        var sent = Assert.Single(factory.SentConfirmationLinks, s => string.Equals(s.Email, email, StringComparison.OrdinalIgnoreCase));
        var confirmationReturnUrl = ExtractQueryParameter(sent.ConfirmationLink, "returnUrl");

        // The bug: this used to resolve to "https://localhost" (redirectUri's bare authority, no path) — a
        // 404 on TodoApi, which maps nothing at "/". It must resolve to scalar's own HomeUri instead, which
        // Program.cs sets to this same "/scalar" redirect_uri.
        Assert.NotEqual(new Uri(redirectUri).GetLeftPart(UriPartial.Authority), confirmationReturnUrl, StringComparer.Ordinal);
        Assert.Equal(redirectUri, confirmationReturnUrl);
    }

    private static string ExtractQueryParameter(string urlOrPath, string name)
    {
        var queryStart = urlOrPath.IndexOf('?');
        var query = QueryHelpers.ParseQuery(queryStart >= 0 ? urlOrPath[queryStart..] : "");

        return query.TryGetValue(name, out var value)
            ? value.ToString()
            : throw new InvalidOperationException($"Query parameter '{name}' not found in '{urlOrPath}'.");
    }
}