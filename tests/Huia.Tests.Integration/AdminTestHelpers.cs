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

    // The only client MapHuiaAdminEndpoints' RequirePresenter("admin-ui") policy accepts (Program.cs) — the
    // admin-endpoint success-path tests need a token minted from this client specifically, distinct from
    // "todo-web" above (which every other caller here, e.g. ReportsEndpointsTests, still relies on).
    private const string AdminUiClientId = "admin-ui";
    private const string AdminUiClientSecret = "admin-ui-dev-secret";
    private const string AdminUiRedirectUri = "http://localhost:3100/auth/oidc/callback";

    private const string Password = "P@ssw0rd123!";

    public static Task<HttpClient> CreateAdminAuthorizedClientAsync(TodoApiFactory factory) =>
        CreateAuthorizedClientAsync(factory, roles: ["Admin"]);

    public static Task<HttpClient> CreateNonAdminAuthorizedClientAsync(TodoApiFactory factory) =>
        CreateAuthorizedClientAsync(factory, roles: []);

    /// <summary>Same as <see cref="CreateAdminAuthorizedClientAsync"/>, also returning the token's <c>sub</c>
    /// claim — the id AuthorizationsEndpoints filters by.</summary>
    public static async Task<(HttpClient Client, string Subject)> CreateAdminAuthorizedClientWithSubjectAsync(
        TodoApiFactory factory)
    {
        var (client, _, subject) = await CreateAuthorizedClientCoreAsync(factory, roles: ["Admin"],
            ClientId, ClientSecret, RedirectUri, oauthScope: "openid profile email todos roles reports");
        return (client, subject);
    }

    public static async Task<HttpClient> CreateAuthorizedClientAsync(TodoApiFactory factory, string[] roles)
    {
        var (client, _, _) = await CreateAuthorizedClientCoreAsync(factory, roles,
            ClientId, ClientSecret, RedirectUri, oauthScope: "openid profile email todos roles reports");
        return client;
    }

    /// <summary>
    /// Same as <see cref="CreateAdminAuthorizedClientAsync"/>, but mints the token from the "admin-ui" client
    /// instead of "todo-web" — the client <c>RequirePresenter("admin-ui")</c> actually lets through. Use this
    /// (not <see cref="CreateAdminAuthorizedClientAsync"/>) for any test expecting a call to
    /// <c>/api/identity/admin/...</c> to succeed.
    /// </summary>
    public static Task<HttpClient> CreateAdminUiAuthorizedClientAsync(TodoApiFactory factory) =>
        CreateAdminUiAuthorizedClientAsync(factory, roles: ["Admin"]);

    public static async Task<HttpClient> CreateAdminUiAuthorizedClientAsync(TodoApiFactory factory, string[] roles)
    {
        // "reports" is omitted (unlike the todo-web helpers above): admin-ui's own AllowScopes (Program.cs)
        // doesn't include it, so requesting it here would make /connect/authorize reject the request outright.
        var (client, _, _) = await CreateAuthorizedClientCoreAsync(factory, roles,
            AdminUiClientId, AdminUiClientSecret, AdminUiRedirectUri, oauthScope: "openid profile email todos roles");
        return client;
    }

    /// <summary>Same as <see cref="CreateAdminUiAuthorizedClientAsync(TodoApiFactory)"/>, also returning the
    /// token's <c>sub</c> claim.</summary>
    public static async Task<(HttpClient Client, string Subject)> CreateAdminUiAuthorizedClientWithSubjectAsync(
        TodoApiFactory factory)
    {
        var (client, _, subject) = await CreateAuthorizedClientCoreAsync(factory, roles: ["Admin"],
            AdminUiClientId, AdminUiClientSecret, AdminUiRedirectUri, oauthScope: "openid profile email todos roles");
        return (client, subject);
    }

    /// <summary>Same as <see cref="CreateAuthorizedClientAsync"/>, also returning the registered email and
    /// subject — for tests (e.g. <c>ManageEndpointsTests</c>) that need to assert against the caller's own
    /// identity rather than just checking status codes. Mints against the "todo-web" client, same as every
    /// other caller in this file that doesn't need the "admin-ui" one specifically.</summary>
    internal static Task<(HttpClient Client, string Email, string Subject)> CreateAuthorizedClientCoreAsync(
        TodoApiFactory factory, string[] roles) =>
        CreateAuthorizedClientCoreAsync(factory, roles,
            ClientId, ClientSecret, RedirectUri, oauthScope: "openid profile email todos roles reports");

    // Long because it's a single linear register-confirm-authorize-redeem sequence; splitting it would just
    // relocate, not reduce, that sequence.
#pragma warning disable MA0051
    private static async Task<(HttpClient Client, string Email, string Subject)> CreateAuthorizedClientCoreAsync(
        TodoApiFactory factory, string[] roles,
        string clientId, string clientSecret, string redirectUri, string oauthScope)
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
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            // "todos" is requested only so the token carries the "todo-api" audience TodoApi's Program.cs
            // requires server-wide (HuiaServerOptions.RequireAudiences) — that check gates authentication
            // itself, before any endpoint-specific authorization runs, so every admin-API call needs it too
            // even though the admin endpoints never touch /api/todos. "reports" (when included in
            // `oauthScope`) is requested so the todo-web client can also satisfy ReportsEndpointsTests'
            // RequireScope("reports")/RequireAudience("reports-api") check.
            $"scope={Uri.EscapeDataString(oauthScope)}",
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
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }));
        tokenResponse.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = document.RootElement.GetProperty("access_token").GetString()!;

        var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (apiClient, email, subject);
    }
#pragma warning restore MA0051

    private static string ExtractQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (string.Equals(parts[0], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"Query parameter '{name}' not found in '{uri}'.");
    }
}
