using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.Entities;
using Huia.Events;
using Huia.Headless.Endpoints;
using Huia.Headless.EntityFrameworkCore;
using Huia.Headless.Identity;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using HuiaDbContext = Huia.Headless.EntityFrameworkCore.HuiaDbContext<Huia.Entities.HuiaUser, Huia.Entities.HuiaRole, string>;

namespace Huia.IntegrationTests;

public sealed class HeadlessAdminEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Admin_endpoints_require_authorization()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("admin/users");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var rolesResponse = await host.Client.GetAsync("admin/roles");
        rolesResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_list_users_and_roles()
    {
        await using var host = await StartAsync();
        var token = await host.CreateAndSignInAdminAsync("admin@test.local", "P@ssword123!");

        var request = new HttpRequestMessage(HttpMethod.Get, "admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var usersPage = await response.Content.ReadFromJsonAsync<UsersPageResponse>(Json);
        usersPage.ShouldNotBeNull();
        usersPage.Data.Count.ShouldBeGreaterThanOrEqualTo(1);
        usersPage.Data.ShouldContain(u => u.Email == "admin@test.local");

        var rolesRequest = new HttpRequestMessage(HttpMethod.Get, "admin/roles");
        rolesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rolesResponse = await host.Client.SendAsync(rolesRequest);
        rolesResponse.EnsureSuccessStatusCode();

        var rolesList = await rolesResponse.Content.ReadFromJsonAsync<RolesResponse>(Json);
        rolesList.ShouldNotBeNull();
    }

    [Fact]
    public async Task Full_user_and_role_management_lifecycle()
    {
        await using var host = await StartAsync();
        var token = await host.CreateAndSignInAdminAsync("admin2@test.local", "P@ssword123!");

        using var client = host.CreateAuthorizedClient(token);

        // 1. Create a dynamic role
        var createRoleRes = await client.PostAsJsonAsync("admin/roles", new { name = "inventory-manager" });
        createRoleRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var role = await createRoleRes.Content.ReadFromJsonAsync<HeadlessRoleDto>(Json);
        role.ShouldNotBeNull();
        role.Name.ShouldBe("inventory-manager");

        // 2. Create user with role
        var createUserRes = await client.PostAsJsonAsync("admin/users", new
        {
            email = "worker@test.local",
            password = "P@ssword123!",
            firstName = "Bob",
            lastName = "Worker",
            roles = new[] { "inventory-manager" }
        });
        createUserRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var user = await createUserRes.Content.ReadFromJsonAsync<HeadlessUserDto>(Json);
        user.ShouldNotBeNull();
        user.Email.ShouldBe("worker@test.local");
        user.Roles.ShouldContain("inventory-manager");

        var regEvt = await host.Events.WaitForAsync<UserRegisteredEvent>(e => e.UserId == user.Id);
        regEvt.ShouldNotBeNull();
        regEvt.Email.ShouldBe("worker@test.local");
        regEvt.Method.ShouldBe(HuiaConstants.AuthenticationMethods.Password);

        // 3. Update user
        var updateUserRes = await client.PutAsJsonAsync($"admin/users/{user.Id}", new
        {
            firstName = "Bobby",
            lastName = "Worker"
        });
        updateUserRes.EnsureSuccessStatusCode();
        var updatedUser = await updateUserRes.Content.ReadFromJsonAsync<HeadlessUserDto>(Json);
        updatedUser!.FirstName.ShouldBe("Bobby");

        await host.Events.WaitForCountAsync<UserUpdatedEvent>(1, e => e.UserId == user.Id);

        // 4. Lock and unlock user
        var lockRes = await client.PostAsync($"admin/users/{user.Id}/lock", null);
        lockRes.EnsureSuccessStatusCode();

        await host.Events.WaitForCountAsync<UserUpdatedEvent>(2, e => e.UserId == user.Id);

        var lockedUserRes = await client.GetAsync($"admin/users/{user.Id}");
        var lockedUser = await lockedUserRes.Content.ReadFromJsonAsync<HeadlessUserDto>(Json);
        lockedUser!.LockoutEnd.ShouldNotBeNull();

        var unlockRes = await client.PostAsync($"admin/users/{user.Id}/unlock", null);
        unlockRes.EnsureSuccessStatusCode();

        await host.Events.WaitForCountAsync<UserUpdatedEvent>(3, e => e.UserId == user.Id);

        var unlockedUserRes = await client.GetAsync($"admin/users/{user.Id}");
        var unlockedUser = await unlockedUserRes.Content.ReadFromJsonAsync<HeadlessUserDto>(Json);
        unlockedUser!.LockoutEnd.ShouldBeNull();

        // 5. Add and remove roles
        var addRoleRes = await client.PostAsJsonAsync($"admin/users/{user.Id}/roles", new { role = "supervisor" });
        addRoleRes.EnsureSuccessStatusCode();

        await host.Events.WaitForCountAsync<UserUpdatedEvent>(4, e => e.UserId == user.Id);

        // Idempotent add role
        var reAddRoleRes = await client.PostAsJsonAsync($"admin/users/{user.Id}/roles", new { role = "supervisor" });
        reAddRoleRes.EnsureSuccessStatusCode();

        var userRolesRes = await client.GetAsync($"admin/users/{user.Id}/roles");
        var roles = await userRolesRes.Content.ReadFromJsonAsync<string[]>(Json);
        roles.ShouldContain("supervisor");

        var removeRoleRes = await client.DeleteAsync($"admin/users/{user.Id}/roles/supervisor");
        removeRoleRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await host.Events.WaitForCountAsync<UserUpdatedEvent>(5, e => e.UserId == user.Id);

        // Idempotent remove role
        var reRemoveRoleRes = await client.DeleteAsync($"admin/users/{user.Id}/roles/supervisor");
        reRemoveRoleRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // 6. Delete user
        var deleteUserRes = await client.DeleteAsync($"admin/users/{user.Id}");
        deleteUserRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var deleteEvt = await host.Events.WaitForAsync<UserDeletedEvent>(e => e.UserId == user.Id);
        deleteEvt.ShouldNotBeNull();

        var getDeletedRes = await client.GetAsync($"admin/users/{user.Id}");
        getDeletedRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 7. Delete role
        var deleteRoleRes = await client.DeleteAsync($"admin/roles/{role.Id}");
        deleteRoleRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Headless_register_endpoint_creates_user_and_publishes_event()
    {
        await using var host = await StartAsync();

        var res = await host.Client.PostAsJsonAsync("identity/register", new
        {
            email = "selfsignup@test.local",
            password = "P@ssword123!",
            firstName = "Self",
            lastName = "Signup"
        });
        res.StatusCode.ShouldBe(HttpStatusCode.OK);

        var regEvt = await host.Events.WaitForAsync<UserRegisteredEvent>(e => e.Email == "selfsignup@test.local");
        regEvt.ShouldNotBeNull();
        regEvt.Method.ShouldBe(HuiaConstants.AuthenticationMethods.Password);
        regEvt.UserName.ShouldBe("selfsignup@test.local");
    }

    private static async Task<HeadlessAdminTestHost> StartAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var eventCollector = new CapturingEventCollector();

        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(connection);
                    services.AddDbContext<HuiaDbContext>(o => o.UseSqlite(connection));

                    services
                        .AddHuiaHeadless(huia =>
                        {
                            huia.UseIssuer("https://headless-admin.test");
                            huia.UseEmailAndPasswordLogin(p => p.RequireConfirmedEmail = false);
                        })
                        .AddEntityFrameworkCoreStores<HuiaDbContext>();

                    services.AddSingleton<Huia.Events.IHuiaEventHandler<Huia.Events.UserRegisteredEvent>>(
                        new CollectingEventHandler<Huia.Events.UserRegisteredEvent>(eventCollector));
                    services.AddSingleton<Huia.Events.IHuiaEventHandler<Huia.Events.UserUpdatedEvent>>(
                        new CollectingEventHandler<Huia.Events.UserUpdatedEvent>(eventCollector));
                    services.AddSingleton<Huia.Events.IHuiaEventHandler<Huia.Events.UserDeletedEvent>>(
                        new CollectingEventHandler<Huia.Events.UserDeletedEvent>(eventCollector));
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHuiaHeadlessEndpoints();
                        endpoints.MapHuiaHeadlessAdminEndpoints().RequireAuthorization();
                    });
                });
            });

        var host = await builder.StartAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<HuiaDbContext>().Database.EnsureCreatedAsync();
        }

        return new HeadlessAdminTestHost(host, connection, eventCollector);
    }

    private sealed class HeadlessAdminTestHost(IHost host, SqliteConnection connection, CapturingEventCollector events) : IAsyncDisposable
    {
        public CapturingEventCollector Events => events;
        public HttpClient Client { get; } = host.GetTestClient();
        public IServiceProvider Services => host.Services;

        public HttpClient CreateAuthorizedClient(string token)
        {
            var client = host.GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public async Task<string> CreateAndSignInAdminAsync(string email, string password)
        {
            await using var scope = Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            var user = new HuiaUser
            {
                UserName = email,
                Email = email,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(user, password);
            result.Succeeded.ShouldBeTrue(string.Join(" ", result.Errors.Select(e => e.Description)));

            var loginRes = await Client.PostAsJsonAsync("identity/login", new { email, password });
            loginRes.EnsureSuccessStatusCode();
            var tokens = await loginRes.Content.ReadFromJsonAsync<TokenResponse>(Json);
            tokens.ShouldNotBeNull();
            tokens.AccessToken.ShouldNotBeNullOrEmpty();
            return tokens.AccessToken;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
            await connection.DisposeAsync();
        }
    }

    private sealed record UsersPageResponse(List<HeadlessUserDto> Data, int TotalCount, int Page, int PageSize, bool HasNext, bool HasPrevious);
    private sealed record RolesResponse(List<HeadlessRoleDto> Data);
    private sealed record TokenResponse(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);
}
