using Huia.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Quartz;

namespace Huia.Tests.Unit.Scheduling;

public class SchedulerBuilderTests
{
    private static OpenIddictQuartzOptions Resolve(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IOptions<OpenIddictQuartzOptions>>().Value;

    [Fact]
    public void SetMinimumAuthorizationLifespan_AppliesToOpenIddictQuartzOptions()
    {
        var services = new ServiceCollection();
        var builder = new SchedulerBuilder(services);

        builder.SetMinimumAuthorizationLifespan(TimeSpan.FromDays(30));

        Assert.Equal(TimeSpan.FromDays(30), Resolve(services).MinimumAuthorizationLifespan);
    }

    [Fact]
    public void SetMinimumTokenLifespan_AppliesToOpenIddictQuartzOptions()
    {
        var services = new ServiceCollection();
        var builder = new SchedulerBuilder(services);

        builder.SetMinimumTokenLifespan(TimeSpan.FromDays(7));

        Assert.Equal(TimeSpan.FromDays(7), Resolve(services).MinimumTokenLifespan);
    }

    [Fact]
    public void SetMaximumRefireCount_AppliesToOpenIddictQuartzOptions()
    {
        var services = new ServiceCollection();
        var builder = new SchedulerBuilder(services);

        builder.SetMaximumRefireCount(5);

        Assert.Equal(5, Resolve(services).MaximumRefireCount);
    }

    [Fact]
    public void DisableAuthorizationPruning_AppliesToOpenIddictQuartzOptions()
    {
        var services = new ServiceCollection();
        var builder = new SchedulerBuilder(services);

        builder.DisableAuthorizationPruning();

        Assert.True(Resolve(services).DisableAuthorizationPruning);
    }

    [Fact]
    public void DisableTokenPruning_AppliesToOpenIddictQuartzOptions()
    {
        var services = new ServiceCollection();
        var builder = new SchedulerBuilder(services);

        builder.DisableTokenPruning();

        Assert.True(Resolve(services).DisableTokenPruning);
    }

    [Fact]
    public void Configure_AppliesArbitraryChangesToOpenIddictQuartzOptions()
    {
        var services = new ServiceCollection();
        var builder = new SchedulerBuilder(services);

        builder.Configure(options => options.MaximumRefireCount = 9);

        Assert.Equal(9, Resolve(services).MaximumRefireCount);
    }

    [Fact]
    public void MultipleCalls_ComposeRatherThanOverwrite()
    {
        var services = new ServiceCollection();
        var builder = new SchedulerBuilder(services);

        builder.SetMinimumAuthorizationLifespan(TimeSpan.FromDays(30));
        builder.SetMinimumTokenLifespan(TimeSpan.FromDays(7));

        var options = Resolve(services);
        Assert.Equal(TimeSpan.FromDays(30), options.MinimumAuthorizationLifespan);
        Assert.Equal(TimeSpan.FromDays(7), options.MinimumTokenLifespan);
    }
}
