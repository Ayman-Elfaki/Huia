using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.EntityFrameworkCore.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Huia.Tests.Persistence;

public sealed class HuiaDbContextModelTests : IDisposable
{
    private readonly HuiaDbContext _context;

    public HuiaDbContextModelTests()
    {
        var options = new DbContextOptionsBuilder<HuiaDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new HuiaDbContext(new StaticMultiTenantContextAccessor<HuiaTenantInfo>(null), options);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Every_table_is_renamed_to_a_Huia_prefix()
    {
        var tables = _context.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(name => name is not null)
            .Distinct()
            .ToList();

        tables.ShouldNotBeEmpty();
        tables.ShouldAllBe(name => name!.StartsWith("Huia", StringComparison.Ordinal));
        tables.ShouldNotContain(name => name!.StartsWith("AspNet", StringComparison.Ordinal));
        tables.ShouldNotContain(name => name!.StartsWith("OpenIddict", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_tables_have_the_expected_names()
    {
        string? Table<T>() => _context.Model.FindEntityType(typeof(T))?.GetTableName();

        Table<HuiaUser>().ShouldBe("HuiaUsers");
        Table<HuiaRole>().ShouldBe("HuiaRoles");
        Table<HuiaSigningKey>().ShouldBe("HuiaSigningKeys");
        _context.Model.GetEntityTypes()
            .First(e => e.ClrType.Name == "OpenIddictEntityFrameworkCoreApplication")
            .GetTableName().ShouldBe("HuiaApplications");
    }

    [Fact]
    public void The_default_identity_global_unique_indexes_are_gone()
    {
        var user = _context.Model.FindEntityType(typeof(HuiaUser))!;

        // No single-column index on NormalizedUserName / NormalizedEmail on its own.
        user.GetIndexes()
            .Any(i => i.Properties.Count == 1 &&
                      (i.Properties[0].Name == "NormalizedUserName" || i.Properties[0].Name == "NormalizedEmail"))
            .ShouldBeFalse();

        var role = _context.Model.FindEntityType(typeof(HuiaRole))!;
        role.GetIndexes()
            .Any(i => i.Properties.Count == 1 && i.Properties[0].Name == "NormalizedName")
            .ShouldBeFalse();
    }

    [Fact]
    public void Tenant_scoped_composite_indexes_are_declared()
    {
        var user = _context.Model.FindEntityType(typeof(HuiaUser))!;

        user.GetIndexes().ShouldContain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "NormalizedUserName" }));

        user.GetIndexes().ShouldContain(i =>
            !i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "NormalizedEmail" }));

        var role = _context.Model.FindEntityType(typeof(HuiaRole))!;
        role.GetIndexes().ShouldContain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "NormalizedName" }));
    }

    [Fact]
    public void TenantId_is_required_on_users_and_roles()
    {
        _context.Model.FindEntityType(typeof(HuiaUser))!.FindProperty(nameof(HuiaUser.TenantId))!
            .IsNullable.ShouldBeFalse();
        _context.Model.FindEntityType(typeof(HuiaRole))!.FindProperty(nameof(HuiaRole.TenantId))!
            .IsNullable.ShouldBeFalse();
    }
}
