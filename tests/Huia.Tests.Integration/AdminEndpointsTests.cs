using System.Net;
using System.Net.Http.Json;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// Covers <c>MapHuiaAdminEndpoints</c> (<see cref="TodoApi.Program"/> gates the whole group behind
/// <c>RequireRole("Admin")</c>, same as <c>ReportsEndpoints</c>): the unauthenticated/non-admin gating each
/// resource group inherits from that gate, plus a full CRUD (or list+revoke, where that's all the API
/// exposes) lifecycle per resource type, exercised against real tokens minted by
/// <see cref="AdminTestHelpers"/> rather than mocked auth.
/// </summary>
public class AdminEndpointsTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string BasePath = "/api/identity/admin";

    [Theory]
    [InlineData("applications")]
    [InlineData("scopes")]
    [InlineData("users")]
    [InlineData("roles")]
    [InlineData("authorizations")]
    public async Task List_WithoutToken_ReturnsUnauthorized(string resource)
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync($"{BasePath}/{resource}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("applications")]
    [InlineData("scopes")]
    [InlineData("users")]
    [InlineData("roles")]
    [InlineData("authorizations")]
    public async Task List_WithoutAdminRole_ReturnsForbidden(string resource)
    {
        var client = await AdminTestHelpers.CreateNonAdminAuthorizedClientAsync(factory);

        using var response = await client.GetAsync($"{BasePath}/{resource}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Applications_Crud_FullLifecycle_Succeeds()
    {
        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);
        var clientId = $"test-app-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync($"{BasePath}/applications", new
        {
            Kind = "spa",
            ClientId = clientId,
            DisplayName = "Test SPA",
            RequiresPkce = true,
            RequireConsent = false,
            RedirectUris = new[] { "https://example.com/callback" },
            Scopes = new[] { "todos" },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var app = await created.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.NotNull(app);
        Assert.Equal(clientId, app!.ClientId);
        Assert.Equal("spa", app.Kind);

        var list = await client.GetFromJsonAsync<PagedResult<ApplicationResponse>>($"{BasePath}/applications");
        Assert.Contains(list!.Items, a => string.Equals(a.ClientId, clientId, StringComparison.Ordinal));

        var fetched = await client.GetFromJsonAsync<ApplicationResponse>($"{BasePath}/applications/{app.Id}");
        Assert.Equal(clientId, fetched!.ClientId);

        var updated = await client.PutAsJsonAsync($"{BasePath}/applications/{app.Id}", new
        {
            Kind = "spa",
            ClientId = clientId,
            DisplayName = "Renamed SPA",
            RequiresPkce = true,
            RequireConsent = false,
            RedirectUris = new[] { "https://example.com/callback" },
            Scopes = new[] { "todos" },
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedApp = await updated.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal("Renamed SPA", updatedApp!.DisplayName);

        var deleted = await client.DeleteAsync($"{BasePath}/applications/{app.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync($"{BasePath}/applications/{app.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Applications_CursorPagination_WalksEveryItemExactlyOnceWithoutTotalCount()
    {
        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);

        // More than one default-sized (25) page, so paging through requires at least one NextCursor hop.
        var createdIds = new List<string>();
        for (var i = 0; i < 30; i++)
        {
            var created = await client.PostAsJsonAsync($"{BasePath}/applications", new
            {
                Kind = "spa",
                ClientId = $"test-app-cursor-{Guid.NewGuid():N}",
                RequiresPkce = true,
                RequireConsent = false,
                RedirectUris = new[] { "https://example.com/callback" },
                Scopes = new[] { "todos" },
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var app = await created.Content.ReadFromJsonAsync<ApplicationResponse>();
            createdIds.Add(app!.Id);
        }

        var firstPage = await client.GetFromJsonAsync<PagedResult<ApplicationResponse>>($"{BasePath}/applications");
        Assert.Equal(25, firstPage!.Items.Count);
        Assert.NotNull(firstPage.NextCursor);

        var walkedIds = new List<string>();
        var cursor = (string?)null;
        do
        {
            var url = cursor is null
                ? $"{BasePath}/applications"
                : $"{BasePath}/applications?cursor={Uri.EscapeDataString(cursor)}";
            var page = await client.GetFromJsonAsync<PagedResult<ApplicationResponse>>(url);
            walkedIds.AddRange(page!.Items.Select(a => a.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(walkedIds.Count, walkedIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(createdIds, id => Assert.Contains(id, walkedIds, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Scopes_Crud_FullLifecycle_Succeeds()
    {
        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);
        var name = $"test-scope-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync($"{BasePath}/scopes", new
        {
            Name = name,
            DisplayName = "Test Scope",
            Description = "A scope created by a test.",
            Resources = new[] { "test-resource" },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var scope = await created.Content.ReadFromJsonAsync<ScopeResponse>();
        Assert.NotNull(scope);
        Assert.Equal(name, scope!.Name);

        var list = await client.GetFromJsonAsync<PagedResult<ScopeResponse>>($"{BasePath}/scopes");
        Assert.Contains(list!.Items, s => string.Equals(s.Name, name, StringComparison.Ordinal));

        var fetched = await client.GetFromJsonAsync<ScopeResponse>($"{BasePath}/scopes/{scope.Id}");
        Assert.Equal(name, fetched!.Name);

        var updated = await client.PutAsJsonAsync($"{BasePath}/scopes/{scope.Id}", new
        {
            Name = name,
            DisplayName = "Renamed Scope",
            Description = "Updated.",
            Resources = new[] { "test-resource" },
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedScope = await updated.Content.ReadFromJsonAsync<ScopeResponse>();
        Assert.Equal("Renamed Scope", updatedScope!.DisplayName);

        var deleted = await client.DeleteAsync($"{BasePath}/scopes/{scope.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync($"{BasePath}/scopes/{scope.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    // Long because it's a single linear create-read-update-role-lockout-reset-delete lifecycle; splitting a
    // test's own sequential steps into multiple methods would just fragment one scenario, not simplify it.
#pragma warning disable MA0051
    public async Task Users_Crud_FullLifecycle_Succeeds()
    {
        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);
        var email = $"admin-crud-{Guid.NewGuid():N}@example.com";

        var created = await client.PostAsJsonAsync($"{BasePath}/users", new
        {
            Email = email,
            Password = "P@ssw0rd123!",
            FirstName = "Crud",
            LastName = "Target",
            EmailConfirmed = true,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = await created.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Equal(email, user!.Email);

        var found = await client.GetFromJsonAsync<PagedResult<UserResponse>>(
            $"{BasePath}/users?search={Uri.EscapeDataString(email)}");
        Assert.Contains(found!.Items, u => string.Equals(u.Id, user.Id, StringComparison.Ordinal));

        var fetched = await client.GetFromJsonAsync<UserResponse>($"{BasePath}/users/{user.Id}");
        Assert.Equal(email, fetched!.Email);

        var updated = await client.PutAsJsonAsync($"{BasePath}/users/{user.Id}", new
        {
            Email = email,
            FirstName = "Updated",
            LastName = "Target",
            EmailConfirmed = true,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedUser = await updated.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("Updated", updatedUser!.FirstName);

        var roleName = $"test-role-{Guid.NewGuid():N}";
        var roleCreated = await client.PostAsJsonAsync($"{BasePath}/roles", new { Name = roleName });
        Assert.Equal(HttpStatusCode.Created, roleCreated.StatusCode);

        var roleAdded = await client.PostAsJsonAsync($"{BasePath}/users/{user.Id}/roles",
            new { Role = roleName, Add = true });
        Assert.Equal(HttpStatusCode.OK, roleAdded.StatusCode);
        var withRole = await roleAdded.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Contains(roleName, withRole!.Roles, StringComparer.Ordinal);

        var roleRemoved = await client.PostAsJsonAsync($"{BasePath}/users/{user.Id}/roles",
            new { Role = roleName, Add = false });
        Assert.Equal(HttpStatusCode.OK, roleRemoved.StatusCode);
        var withoutRole = await roleRemoved.Content.ReadFromJsonAsync<UserResponse>();
        Assert.DoesNotContain(roleName, withoutRole!.Roles, StringComparer.Ordinal);

        var lockedOut = await client.PostAsJsonAsync($"{BasePath}/users/{user.Id}/lockout", new { Locked = true });
        Assert.Equal(HttpStatusCode.OK, lockedOut.StatusCode);
        var lockedUser = await lockedOut.Content.ReadFromJsonAsync<UserResponse>();
        Assert.True(lockedUser!.IsLockedOut);

        var unlocked = await client.PostAsJsonAsync($"{BasePath}/users/{user.Id}/lockout", new { Locked = false });
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        var unlockedUser = await unlocked.Content.ReadFromJsonAsync<UserResponse>();
        Assert.False(unlockedUser!.IsLockedOut);

        var resetPassword = await client.PostAsJsonAsync($"{BasePath}/users/{user.Id}/reset-password",
            new { NewPassword = "N3wP@ssw0rd!" });
        Assert.Equal(HttpStatusCode.NoContent, resetPassword.StatusCode);

        var deleted = await client.DeleteAsync($"{BasePath}/users/{user.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync($"{BasePath}/users/{user.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }
#pragma warning restore MA0051

    /// <summary>
    /// The admin API's own view of which external (third-party) provider(s) a user signed in with, and
    /// their avatar — the same data <c>ManageExternalLoginsEndpoints</c> exposes for self-service, surfaced
    /// here for an admin looking at someone else's account. Links the login directly via
    /// <c>UserManager.AddLoginAsync</c> rather than driving a real OAuth round trip — that mechanism itself
    /// is covered by <c>ExternalLoginTests</c>; this test is only about the admin endpoint's response shape.
    /// </summary>
    [Fact]
    public async Task Users_Get_ReflectsExternalLoginsAndPicture()
    {
        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);
        var email = $"admin-external-{Guid.NewGuid():N}@example.com";

        var created = await client.PostAsJsonAsync($"{BasePath}/users", new
        {
            Email = email,
            Password = "P@ssw0rd123!",
            FirstName = "External",
            LastName = "Target",
            EmailConfirmed = true,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = await created.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.True(user!.HasPassword);
        Assert.Empty(user.ExternalLogins);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var huiaUser = await userManager.FindByIdAsync(user.Id) ?? throw new InvalidOperationException("User not found.");
            huiaUser.Picture = "https://example.com/avatar.png";
            await userManager.UpdateAsync(huiaUser);
            await userManager.AddLoginAsync(huiaUser, new UserLoginInfo("google", "fake-provider-key", "Google"));
        }

        var fetched = await client.GetFromJsonAsync<UserResponse>($"{BasePath}/users/{user.Id}");
        Assert.Equal("https://example.com/avatar.png", fetched!.Picture);
        var login = Assert.Single(fetched.ExternalLogins);
        Assert.Equal("google", login.LoginProvider);
        Assert.Equal("Google", login.ProviderDisplayName);
    }

    [Fact]
    public async Task Roles_Crud_FullLifecycle_Succeeds()
    {
        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);
        var name = $"test-role-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync($"{BasePath}/roles", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = await created.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.NotNull(role);
        Assert.Equal(name, role!.Name);

        var list = await client.GetFromJsonAsync<PagedResult<RoleResponse>>($"{BasePath}/roles");
        Assert.Contains(list!.Items, r => string.Equals(r.Name, name, StringComparison.Ordinal));

        var fetched = await client.GetFromJsonAsync<RoleResponse>($"{BasePath}/roles/{role.Id}");
        Assert.Equal(name, fetched!.Name);

        var members = await client.GetFromJsonAsync<List<RoleMemberResponse>>($"{BasePath}/roles/{role.Id}/members");
        Assert.Empty(members!);

        var renamedName = $"{name}-renamed";
        var renamed = await client.PutAsJsonAsync($"{BasePath}/roles/{role.Id}", new { Name = renamedName });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var renamedRole = await renamed.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.Equal(renamedName, renamedRole!.Name);

        var deleted = await client.DeleteAsync($"{BasePath}/roles/{role.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync($"{BasePath}/roles/{role.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    /// <summary>
    /// AdminTestHelpers' authorization_code exchange (used to get the admin's own bearer token) itself
    /// creates a live OpenIddict authorization for the admin's subject/the "todo-web" client — that's the
    /// fixture this test lists/revokes rather than seeding one separately, since authorizations can only be
    /// created by a real OAuth flow (AuthorizationsEndpoints has no create endpoint).
    /// </summary>
    [Fact]
    public async Task Authorizations_List_GetAndRevoke_Succeeds()
    {
        var (client, subject) = await AdminTestHelpers.CreateAdminAuthorizedClientWithSubjectAsync(factory);

        var list = await client.GetFromJsonAsync<PagedResult<AuthorizationResponse>>(
            $"{BasePath}/authorizations?subject={Uri.EscapeDataString(subject)}");
        Assert.NotEmpty(list!.Items);
        var authorization = list.Items[0];

        var fetched = await client.GetFromJsonAsync<AuthorizationResponse>(
            $"{BasePath}/authorizations/{authorization.Id}");
        Assert.Equal(authorization.Id, fetched!.Id);

        var revoked = await client.PostAsync($"{BasePath}/authorizations/{authorization.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        // Revoking marks the authorization as revoked rather than deleting it.
        var afterRevoke = await client.GetAsync($"{BasePath}/authorizations/{authorization.Id}");
        Assert.Equal(HttpStatusCode.OK, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task Authorizations_Revoke_UnknownId_ReturnsNotFound()
    {
        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);

        var response = await client.PostAsync($"{BasePath}/authorizations/does-not-exist/revoke", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ApplicationResponse(
        string Id,
        string ClientId,
        string? DisplayName,
        string Kind,
        string? ApplicationType,
        string? ClientType,
        string? ConsentType,
        string[] RedirectUris,
        string[] PostLogoutRedirectUris,
        string[] Permissions,
        string[] Requirements,
        TimeSpan? AccessTokenLifetime,
        TimeSpan? IdentityTokenLifetime,
        TimeSpan? RefreshTokenLifetime);

    private sealed record ScopeResponse(string Id, string Name, string? DisplayName, string? Description, string[] Resources);

    private sealed record UserResponse(
        string Id,
        string Email,
        string? FirstName,
        string? LastName,
        string? Picture,
        bool EmailConfirmed,
        string? PhoneNumber,
        bool IsLockedOut,
        bool TwoFactorEnabled,
        bool HasPassword,
        string[] Roles,
        ExternalLoginResponse[] ExternalLogins);

    private sealed record ExternalLoginResponse(string LoginProvider, string? ProviderDisplayName);

    private sealed record RoleResponse(string Id, string Name);

    private sealed record RoleMemberResponse(string Id, string Email);

    private sealed record AuthorizationResponse(
        string Id,
        string? ApplicationClientId,
        string? Subject,
        string? Status,
        string? Type,
        DateTimeOffset? CreationDate,
        string[] Scopes);
}
