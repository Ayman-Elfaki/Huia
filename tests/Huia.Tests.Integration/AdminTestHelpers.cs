using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// Drives a full <c>authorization_code</c> exchange against the confidential "todo-web" client (no PKCE — it
/// never calls RequirePkce) to get a real user access token, optionally promoted to one or more roles first —
/// the pattern <c>ReportsEndpointsTests</c> established for its own Admin-role checks, generalized here so
/// <c>AdminEndpointsTests</c> can also get a plain (non-admin) authenticated client.
/// </summary>
internal static class AdminTestHelpers
{
    private const string ClientId = "todo-web";
    private const string ClientSecret = "todo-web-dev-secret";
    private const string RedirectUri = "http://localhost:3000/api/auth/callback/huia";
    private const string Password = "P@ssw0rd123!";

    public static Task<HttpClient> CreateAdminAuthorizedClientAsync(TodoApiFactory factory) =>
        CreateAuthorizedClientAsync(factory, roles: ["Admin"]);

    public static Task<HttpClient> CreateNonAdminAuthorizedClientAsync(TodoApiFactory factory) =>
        CreateAuthorizedClientAsync(factory, roles: []);

    /// <summary>Same as <see cref="CreateAdminAuthorizedClientAsync"/>, also returning the token's <c>sub</c>
    /// claim — the id AuthorizationsEndpoints/SessionsEndpoints filter by.</summary>
    public static async Task<(HttpClient Client, string Subject)> CreateAdminAuthorizedClientWithSubjectAsync(
        TodoApiFactory factory)
    {
        var (client, _, subject) = await CreateAuthorizedClientCoreAsync(factory, roles: ["Admin"]);
        return (client, subject);
    }

    public static async Task<HttpClient> CreateAuthorizedClientAsync(TodoApiFactory factory, string[] roles)
    {
        var (client, _, _) = await CreateAuthorizedClientCoreAsync(factory, roles);
        return client;
    }

    /// <summary>Same as <see cref="CreateAuthorizedClientAsync"/>, also returning the registered email and
    /// subject — for tests (e.g. <c>ManageEndpointsTests</c>) that need to assert against the caller's own
    /// identity rather than just checking status codes.</summary>
    internal static async Task<(HttpClient Client, string Email, string Subject)> CreateAuthorizedClientCoreAsync(
        TodoApiFactory factory, string[] roles)
    {
        var uiClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var email = $"test-{Guid.NewGuid():N}@example.com";
        await IdentityUiTestHelpers.RegisterAsync(uiClient, email, Password);

        string subject;
        using (var scope = factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<HuiaRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var user = await userManager.FindByEmailAsync(email)
                       ?? throw new InvalidOperationException("Registered user not found.");
            subject = user.Id;

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new HuiaRole { Name = role });
                }

                await userManager.AddToRoleAsync(user, role);
            }
        }

        var query = string.Join('&', new[]
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(RedirectUri)}",
            // "todos" is requested only so the token carries the "todo-api" audience TodoApi's Program.cs
            // requires server-wide (HuiaServerOptions.RequireAudiences) — that check gates authentication
            // itself, before any endpoint-specific authorization runs, so every admin-API call needs it too
            // even though the admin endpoints never touch /api/todos. "reports" is requested so this same
            // client can also satisfy ReportsEndpointsTests' RequireScope("reports")/RequireAudience
            // ("reports-api") check.
            $"scope={Uri.EscapeDataString("openid profile email todos roles reports")}",
            $"state={Guid.NewGuid():N}",
        });
        using var authorizeResponse = await uiClient.GetAsync($"/connect/authorize?{query}");
        if (authorizeResponse.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException(
                $"Expected a redirect from /connect/authorize, got {authorizeResponse.StatusCode}.");
        }

        var code = ExtractQueryParameter(authorizeResponse.Headers.Location!, "code");

        using var tokenResponse = await uiClient.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
            }));
        tokenResponse.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = document.RootElement.GetProperty("access_token").GetString()!;

        var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (apiClient, email, subject);
    }

    private static string ExtractQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"Query parameter '{name}' not found in '{uri}'.");
    }
}
