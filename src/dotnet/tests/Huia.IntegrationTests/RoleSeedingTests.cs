using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huia.EntityFrameworkCore;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.OpenId.EntityFrameworkCore;
using Huia.OpenId.Identity;
using Huia.IntegrationTests.Infrastructure;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Huia.IntegrationTests;

public sealed class RoleSeedingTests : IAsyncLifetime
{
    private const string RedirectUri = "https://admin.example.test/callback";
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureOptions: huia =>
        {
            huia.AddTenant("roled", tenant =>
            {
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
                tenant.AddRoles("editor", "beta-tester");
            });
            huia.AddTenant("unroled", tenant =>
                tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false));
        });

        await _host.SeedInteractiveClientAsync("master", "master-spa", RedirectUri);
        var adminId = await _host.SeedUserAsync("master", "root@master.test", "Password1!", emailConfirmed: true);
        await _host.AssignRoleAsync("master", adminId, HuiaConstants.Roles.Administrator);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpClient> AdminApiAsync()
    {
        var flow = new AuthCodeFlow(_host, "master", "master-spa", RedirectUri);
        using var tokens = await flow.SignInAsync("root@master.test", "Password1!", "openid profile email roles");
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());
        return client;
    }

    [Fact]
    public async Task Declared_roles_exist_at_start_up_without_being_created_through_the_admin_api()
    {
        var (editorExists, betaTesterExists) = await WithRoleManagerAsync("roled", async roleManager =>
            (await roleManager.RoleExistsAsync("editor"), await roleManager.RoleExistsAsync("beta-tester")));

        editorExists.ShouldBeTrue();
        betaTesterExists.ShouldBeTrue();
    }

    [Fact]
    public async Task A_role_declared_for_one_tenant_does_not_leak_into_another()
    {
        var exists = await WithRoleManagerAsync("unroled", roleManager => roleManager.RoleExistsAsync("editor"));

        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task A_user_can_be_assigned_a_declaratively_seeded_role()
    {
        var userId = await _host.SeedUserAsync("roled", "sam@roled.test", "Password1!");

        var succeeded = await _host.WithUserManagerAsync("roled", async userManager =>
        {
            var user = await userManager.FindByIdAsync(userId) ?? throw new InvalidOperationException("user not found");
            var result = await userManager.AddToRoleAsync(user, "editor");
            return result.Succeeded;
        });

        succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_declaratively_seeded_role_is_stamped_static()
    {
        var origin = await WithRoleManagerAsync("roled", async roleManager =>
        {
            var role = await roleManager.FindByNameAsync("editor")
                       ?? throw new InvalidOperationException("The seeded role was not found.");
            return role.Origin;
        });

        origin.ShouldBe(HuiaConstants.Origins.Static);
    }

    [Fact]
    public async Task A_declaratively_seeded_role_cannot_be_renamed_or_deleted_through_the_admin_api()
    {
        var id = await WithRoleManagerAsync("roled", async roleManager =>
            (await roleManager.FindByNameAsync("editor")
             ?? throw new InvalidOperationException("The seeded role was not found.")).Id);

        using var client = await AdminApiAsync();

        var rename = await client.PutAsJsonAsync($"/master/admin/roles/{id}", new { name = "editor-renamed" });
        rename.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var delete = await client.DeleteAsync($"/master/admin/roles/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_dynamically_created_role_in_the_same_tenant_stays_editable()
    {
        using var client = await AdminApiAsync();

        var create = await client.PostAsJsonAsync("/master/admin/roles", new { tenant = "roled", name = "temp.role" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var rename = await client.PutAsJsonAsync($"/master/admin/roles/{id}", new { name = "temp.role.renamed" });
        rename.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var delete = await client.DeleteAsync($"/master/admin/roles/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------ pruning

    [Fact]
    public async Task Pruning_deletes_a_static_role_no_longer_declared_in_its_still_configured_tenant()
    {
        await WithRoleManagerAsync("roled", async roleManager =>
        {
            await roleManager.CreateAsync(new HuiaRole("ghost.role") { TenantId = "roled", Origin = HuiaConstants.Origins.Static });
            return true;
        });

        await RunRoleSeederAsync(pruneRemovedStaticEntities: true);

        var exists = await WithRoleManagerAsync("roled", roleManager => roleManager.RoleExistsAsync("ghost.role"));
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task Pruning_deletes_a_static_role_whose_entire_tenant_was_removed_from_config()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        using (HuiaTenantScope.Enter(scope.ServiceProvider, "ghost-tenant"))
        {
            var db = scope.ServiceProvider.GetRequiredService<HuiaDbContext>();
            db.Add(new HuiaRole("ghost.role") { TenantId = "ghost-tenant", Origin = HuiaConstants.Origins.Static });
            await db.SaveChangesAsync();
        }

        await RunRoleSeederAsync(pruneRemovedStaticEntities: true);

        await using var verify = _host.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<HuiaDbContext>();
        var stillThere = await verifyDb.Set<HuiaRole>().IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == "ghost-tenant" && r.Name == "ghost.role");
        stillThere.ShouldBeFalse();
    }

    [Fact]
    public async Task Pruning_skips_a_static_role_that_still_has_members()
    {
        var userId = await _host.SeedUserAsync("roled", "guarded@roled.test", "Password1!");
        await WithRoleManagerAsync("roled", async roleManager =>
        {
            await roleManager.CreateAsync(new HuiaRole("guarded.role") { TenantId = "roled", Origin = HuiaConstants.Origins.Static });
            return true;
        });
        await _host.WithUserManagerAsync("roled", async userManager =>
        {
            var user = await userManager.FindByIdAsync(userId) ?? throw new InvalidOperationException("user not found");
            return (await userManager.AddToRoleAsync(user, "guarded.role")).Succeeded;
        });

        await RunRoleSeederAsync(pruneRemovedStaticEntities: true);

        var stillExists = await WithRoleManagerAsync("roled", roleManager => roleManager.RoleExistsAsync("guarded.role"));
        stillExists.ShouldBeTrue();
    }

    [Fact]
    public async Task Pruning_is_off_by_default_and_leaves_an_undeclared_static_role_alone()
    {
        await WithRoleManagerAsync("roled", async roleManager =>
        {
            await roleManager.CreateAsync(new HuiaRole("ghost.role") { TenantId = "roled", Origin = HuiaConstants.Origins.Static });
            return true;
        });

        await RunRoleSeederAsync(pruneRemovedStaticEntities: false);

        var exists = await WithRoleManagerAsync("roled", roleManager => roleManager.RoleExistsAsync("ghost.role"));
        exists.ShouldBeTrue();
    }

    /// <summary>Re-runs the role seeder's start-up pass on demand, with the given pruning setting.</summary>
    private async Task RunRoleSeederAsync(bool pruneRemovedStaticEntities)
    {
        _host.Services.GetRequiredService<HuiaOptions>().Seeding.PruneRemovedStaticEntities = pruneRemovedStaticEntities;
        var seeder = _host.Services.GetServices<IHostedService>().OfType<HuiaRoleSeeder>().Single();
        await seeder.StartAsync(CancellationToken.None);
    }

    private async Task<T> WithRoleManagerAsync<T>(string tenantId, Func<RoleManager<HuiaRole>, Task<T>> work)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<HuiaRole>>();
            return await work(roleManager);
        }
    }
}
