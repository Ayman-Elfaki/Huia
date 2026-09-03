using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.EntityFrameworkCore.Multitenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Huia.Tests.Persistence;

/// <summary>
/// <see cref="HuiaDbContext"/> derives from Finbuckle's <c>MultiTenantIdentityDbContext</c>: reads are
/// filtered to the context's tenant and writes of an entity that belongs to another tenant — or to no
/// tenant at all — are rejected.
/// </summary>
public sealed class HuiaDbContextTenantFilterTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = ContextFor(null);
        await context.Database.EnsureCreatedAsync();

        await SeedUserAsync("acme", "alice");
        await SeedUserAsync("contoso", "alice");
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Reads_are_filtered_to_the_contexts_tenant()
    {
        await using var acme = ContextFor("acme");

        var users = await acme.Users.ToListAsync();

        users.ShouldHaveSingleItem().TenantId.ShouldBe("acme");
    }

    [Fact]
    public async Task A_cross_tenant_row_is_invisible_even_when_queried_by_id()
    {
        string contosoUserId;
        await using (var contoso = ContextFor("contoso"))
        {
            contosoUserId = (await contoso.Users.SingleAsync()).Id;
        }

        await using var acme = ContextFor("acme");
        (await acme.Users.FirstOrDefaultAsync(u => u.Id == contosoUserId)).ShouldBeNull();

        // …but IgnoreQueryFilters() still reaches it (the admin-console path).
        (await acme.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == contosoUserId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Saving_a_multi_tenant_entity_with_no_tenant_in_scope_throws()
    {
        await using var context = ContextFor(null);
        context.Users.Add(new HuiaUser { UserName = "mallory", NormalizedUserName = "MALLORY" });

        await Should.ThrowAsync<MultiTenantException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Saving_a_row_stamped_for_another_tenant_throws()
    {
        await using var acme = ContextFor("acme");
        acme.Users.Add(new HuiaUser { TenantId = "contoso", UserName = "mallory", NormalizedUserName = "MALLORY2" });

        await Should.ThrowAsync<MultiTenantException>(() => acme.SaveChangesAsync());
    }

    private async Task SeedUserAsync(string tenantId, string userName)
    {
        await using var context = ContextFor(tenantId);
        context.Users.Add(new HuiaUser
        {
            TenantId = tenantId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@{tenantId}.test",
            NormalizedEmail = $"{userName}@{tenantId}.test".ToUpperInvariant(),
        });
        (await context.SaveChangesAsync()).ShouldBe(1);
    }

    private HuiaDbContext ContextFor(string? tenantId)
    {
        var accessor = new StaticMultiTenantContextAccessor<HuiaTenantInfo>(
            tenantId is null ? null : new HuiaTenantInfo(tenantId));
        var options = new DbContextOptionsBuilder<HuiaDbContext>().UseSqlite(_connection).Options;
        return new HuiaDbContext(accessor, options);
    }
}
