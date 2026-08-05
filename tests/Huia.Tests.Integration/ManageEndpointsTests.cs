using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// Covers <c>MapHuiaManageEndpoints</c>'s auth policy: it accepts either the <c>Identity.Application</c>
/// cookie or a bearer access token (see <see cref="EndpointRouteBuilderExtensions"/>). This exercises the
/// bearer path specifically, since that's what a cross-origin OAuth client (the Todo App, the Admin UI) uses
/// — via the same <see cref="AdminTestHelpers"/> token-minting helper <see cref="AdminEndpointsTests"/> uses
/// — plus one cookie-path test as a regression guard for the pre-existing same-origin usage.
/// </summary>
public class ManageEndpointsTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string InfoPath = "/api/identity/manage/info";

    [Fact]
    public async Task GetInfo_WithoutCredentials_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync(InfoPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInfo_WithBearerToken_ReturnsOwnInfo()
    {
        var (client, email, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        using var response = await client.GetAsync(InfoPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Equal(email, info!.Email);
    }

    [Fact]
    public async Task UpdateInfo_WithBearerToken_PersistsChange()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        using var response = await client.PostAsJsonAsync(InfoPath, new
        {
            FirstName = "Updated",
            LastName = "Name",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Equal("Updated", info!.FirstName);
        Assert.Equal("Name", info.LastName);
    }

    /// <summary>Regression guard for the pre-existing same-origin (server-rendered) usage this endpoint group
    /// was originally built for, alongside the bearer path added for cross-origin OAuth clients above.</summary>
    [Fact]
    public async Task GetInfo_WithCookie_ReturnsOwnInfo()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var email = $"cookie-test-{Guid.NewGuid():N}@example.com";
        await IdentityUiTestHelpers.RegisterAsync(client, email, "P@ssw0rd123!");

        using var response = await client.GetAsync(InfoPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Equal(email, info!.Email);
    }

    private const string SessionsPath = "/api/identity/manage/sessions";

    /// <summary>
    /// The registration + authorization_code exchange <see cref="AdminTestHelpers"/> drives creates exactly
    /// one live session for the resulting subject (same fixture <c>AdminEndpointsTests</c>' own session
    /// tests rely on) — the one backing the bearer token this test calls the endpoint with, so it must come
    /// back marked <c>isCurrent</c>.
    /// </summary>
    [Fact]
    public async Task GetSessions_ReturnsOwnSessionMarkedCurrent()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        var sessions = await client.GetFromJsonAsync<List<SessionResponse>>(SessionsPath);

        var session = Assert.Single(sessions!);
        Assert.True(session.IsCurrent);
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task RevokeSession_UnknownId_ReturnsNotFound()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        var response = await client.PostAsync($"{SessionsPath}/does-not-exist/revoke", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A caller can only revoke their own sessions — someone else's (valid) session id must 404,
    /// the same "don't confirm existence" response as an id that doesn't exist at all.</summary>
    [Fact]
    public async Task RevokeSession_BelongingToAnotherUser_ReturnsNotFound()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);
        var (_, _, otherSubject) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        using var scope = factory.Services.CreateScope();
        var sessionManager = scope.ServiceProvider.GetRequiredService<Huia.Sessions.IUserSessionManager>();
        var otherSessions = await sessionManager.ListAsync(1, 0, otherSubject, CancellationToken.None);
        var otherSessionId = Assert.Single(otherSessions).Id;

        var response = await client.PostAsync($"{SessionsPath}/{otherSessionId}/revoke", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>"Sign out of all other devices" must never revoke the very session making the request.</summary>
    [Fact]
    public async Task RevokeOthers_NeverRevokesTheCallingSession()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        var revoked = await client.PostAsync($"{SessionsPath}/revoke-others", content: null);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var sessions = await client.GetFromJsonAsync<List<SessionResponse>>(SessionsPath);
        var session = Assert.Single(sessions!);
        Assert.True(session.IsCurrent);
        Assert.Null(session.RevokedAt);
    }

    private sealed record InfoResponse(string Email, bool IsEmailConfirmed, string? FirstName, string? LastName);

    private sealed record SessionResponse(
        string Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt,
        DateTimeOffset ExpiresAt,
        string? IpAddress,
        string? UserAgent,
        DateTimeOffset? RevokedAt,
        bool IsCurrent,
        string[] ApplicationClientIds);
}
