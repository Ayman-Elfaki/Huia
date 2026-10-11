using Huia.Multitenancy;
using Huia.Options;
using Xunit;

namespace Huia.Tests;

public class HuiaTenantProviderTests
{
    [Fact]
    public void The_default_provider_mirrors_the_options_tree()
    {
        var options = new HuiaOptions();
        options.Tenants["acme"] = new HuiaTenant("acme");
        options.Tenants["globex"] = new HuiaTenant("globex");

        IHuiaTenantProvider provider = new HuiaOptionsTenantProvider(options);

        Assert.Equal(2, provider.Count);
        Assert.True(provider.ContainsKey("acme"));
        Assert.False(provider.ContainsKey("initech"));
        Assert.True(provider.TryGetValue("globex", out var globex));
        Assert.Same(options.Tenants["globex"], globex);
        Assert.Equal(["acme", "globex"], provider.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_default_provider_sees_tenants_added_to_the_tree_after_it_was_created()
    {
        var options = new HuiaOptions();
        IHuiaTenantProvider provider = new HuiaOptionsTenantProvider(options);

        Assert.Empty(provider);
        options.Tenants["late"] = new HuiaTenant("late");

        Assert.True(provider.ContainsKey("late"));
    }
}
