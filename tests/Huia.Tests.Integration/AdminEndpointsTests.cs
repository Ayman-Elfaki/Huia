using System.Net;
using System.Net.Http.Json;
using Huia.Common;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// Covers <c>MapHuiaAdminEndpoints</c> (<see cref="TodoApi.Program"/> gates the whole group behind both
/// <c>RequireRole("Admin")</c>, same as <c>ReportsEndpoints</c>, and <c>RequirePresenter("admin-ui")</c> so
/// only the admin console's own client can reach it): the unauthenticated/non-admin/wrong-client gating each
/// resource group inherits from that gate, plus a full CRUD (or list+revoke, where that's all the API
/// exposes) lifecycle per resource type, exercised against real tokens minted by
/// <see cref="AdminTestHelpers"/> (from the "admin-ui" client specifically — the one client this policy lets
/// through) rather than mocked auth.
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

    /// <summary>
    /// An Admin-role user's token still isn't enough on its own — <c>RequirePresenter("admin-ui")</c> (see
    /// this class' own doc comment) rejects it unless it was minted for the admin-ui client specifically.
    /// "todo-web" stands in for any other client here: it requests the same "roles" scope as admin-ui (see
    /// its own <c>AllowScopes</c> in Program.cs), so without RequirePresenter its Admin-role users could
    /// otherwise reach these endpoints too.
    /// </summary>
    [Theory]
    [InlineData("applications")]
    [InlineData("scopes")]
    [InlineData("users")]
    [InlineData("roles")]
    [InlineData("authorizations")]
    public async Task List_WithAdminRoleFromAnotherClient_ReturnsForbidden(string resource)
    {
        var client = await AdminTestHelpers.CreateAdminAuthorizedClientAsync(factory);

        using var response = await client.GetAsync($"{BasePath}/{resource}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Long because the page-walk needed to make the list assertion immune to sibling tests' seed data
    // (see the comment at the walk itself) pushes this past the line-count threshold; splitting a single
    // linear CRUD lifecycle into multiple methods would fragment one scenario, not simplify it.
#pragma warning disable MA0051
    public async Task Applications_Crud_FullLifecycle_Succeeds()
    {
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);
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
        
        // Ordered by id (a random GUID assigned at creation), not insertion order, so with other tests in
        // this shared fixture having created dozens of applications already, the one just created here could
        // land anywhere - the max page size keeps this assertion reliable regardless of test execution order.
        var list = await client.GetFromJsonAsync<KeysetPage<ApplicationResponse>>($"{BasePath}/applications?pageSize=100");
        Assert.Contains(list!.Data, a => string.Equals(a.ClientId, clientId, StringComparison.Ordinal));

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
#pragma warning restore MA0051

    [Fact]
    public async Task Applications_KeysetPagination_WalksEveryItemExactlyOnceWithoutTotalCount()
    {
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);

        // More than one default-sized (25) page, so paging through requires at least one "after" hop.
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

        var firstPage = await client.GetFromJsonAsync<KeysetPage<ApplicationResponse>>($"{BasePath}/applications");
        Assert.Equal(25, firstPage!.Data.Count);
        Assert.True(firstPage.HasNext);

        // MR.AspNetCore.Pagination doesn't hand back an opaque cursor token - the caller derives the next
        // page's "after" value from the id of the last item it already has.
        var walkedIds = new List<string>(firstPage.Data.Select(a => a.Id));
        var hasNext = firstPage.HasNext;
        var lastId = firstPage.Data[^1].Id;
        while (hasNext)
        {
            var page = await client.GetFromJsonAsync<KeysetPage<ApplicationResponse>>(
                $"{BasePath}/applications?after={Uri.EscapeDataString(lastId)}");
            walkedIds.AddRange(page!.Data.Select(a => a.Id));
            hasNext = page.HasNext;
            if (page.Data.Count > 0)
            {
                lastId = page.Data[^1].Id;
            }
        }

        Assert.Equal(walkedIds.Count, walkedIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(createdIds, id => Assert.Contains(id, walkedIds, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Scopes_Crud_FullLifecycle_Succeeds()
    {
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);
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

        var list = await client.GetFromJsonAsync<KeysetPage<ScopeResponse>>($"{BasePath}/scopes");
        Assert.Contains(list!.Data, s => string.Equals(s.Name, name, StringComparison.Ordinal));

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
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);
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

        var found = await client.GetFromJsonAsync<KeysetPage<UserResponse>>(
            $"{BasePath}/users?search={Uri.EscapeDataString(email)}");
        Assert.Contains(found!.Data, u => string.Equals(u.Id, user.Id, StringComparison.Ordinal));

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

    [Theory]
    [InlineData(null, "Valid")]
    [InlineData("", "Valid")]
    [InlineData("Ada99", "Valid")]
    [InlineData("Valid", null)]
    [InlineData("Valid", "")]
    [InlineData("Valid", "Ada_Lovelace")]
    public async Task Users_Create_WithInvalidName_ReturnsValidationProblem(string? firstName, string? lastName)
    {
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);

        var response = await client.PostAsJsonAsync($"{BasePath}/users", new
        {
            Email = $"admin-invalid-name-{Guid.NewGuid():N}@example.com",
            Password = "P@ssw0rd123!",
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_Update_WithInvalidName_ReturnsValidationProblemAndLeavesUserUnchanged()
    {
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);
        var email = $"admin-update-invalid-{Guid.NewGuid():N}@example.com";

        var created = await client.PostAsJsonAsync($"{BasePath}/users", new
        {
            Email = email,
            Password = "P@ssw0rd123!",
            FirstName = "Valid",
            LastName = "Name",
            EmailConfirmed = true,
        });
        var user = await created.Content.ReadFromJsonAsync<UserResponse>();

        var updated = await client.PutAsJsonAsync($"{BasePath}/users/{user!.Id}", new
        {
            Email = email,
            FirstName = "Invalid123",
            LastName = "Name",
            EmailConfirmed = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, updated.StatusCode);

        var fetched = await client.GetFromJsonAsync<UserResponse>($"{BasePath}/users/{user.Id}");
        Assert.Equal("Valid", fetched!.FirstName);
    }

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
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);
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
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);
        var name = $"test-role-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync($"{BasePath}/roles", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = await created.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.NotNull(role);
        Assert.Equal(name, role!.Name);

        var list = await client.GetFromJsonAsync<KeysetPage<RoleResponse>>($"{BasePath}/roles");
        Assert.Contains(list!.Data, r => string.Equals(r.Name, name, StringComparison.Ordinal));

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
    /// creates a live OpenIddict authorization for the admin's subject/the "admin-ui" client — that's the
    /// fixture this test lists/revokes rather than seeding one separately, since authorizations can only be
    /// created by a real OAuth flow (AuthorizationsEndpoints has no create endpoint).
    /// </summary>
    [Fact]
    public async Task Authorizations_List_GetAndRevoke_Succeeds()
    {
        var (client, subject) = await AdminTestHelpers.CreateAdminUiAuthorizedClientWithSubjectAsync(factory);

        var list = await client.GetFromJsonAsync<KeysetPage<AuthorizationResponse>>(
            $"{BasePath}/authorizations?subject={Uri.EscapeDataString(subject)}");
        Assert.NotEmpty(list!.Data);
        var authorization = list.Data[0];

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
        var client = await AdminTestHelpers.CreateAdminUiAuthorizedClientAsync(factory);

        var response = await client.PostAsync($"{BasePath}/authorizations/does-not-exist/revoke", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Mirrors MR.AspNetCore.Pagination's <c>KeysetPaginationResult&lt;T&gt;</c> - also matches the
    /// unpaginated shape <c>RolesEndpoints</c> returns for consistency (see its own local <c>RolesPage&lt;T&gt;</c>
    /// mirror), so this one type covers every admin list endpoint's response.</summary>
    private sealed record KeysetPage<T>(IReadOnlyList<T> Data, int TotalCount, int PageSize, bool HasPrevious,
        bool HasNext);

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
