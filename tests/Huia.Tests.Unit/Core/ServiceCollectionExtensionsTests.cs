using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Huia.Tests.Unit.Core;

/// <summary>
/// Covers <c>AddHuia</c>'s requirement that at least one 1FA sign-in method be explicitly enabled — no
/// implicit default, since which method(s) an app accepts is a conscious choice.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    private static HuiaOptions AddHuiaAndResolveOptions(Action<HuiaOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHuia("https://localhost", configure);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<HuiaOptions>();
    }

    [Fact]
    public void AddHuia_NeitherFlowConfigured_Throws()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddHuia("https://localhost", _ => { }));

        Assert.Contains("UseEmailAndPasswordFlow", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UsePasswordlessFlow", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddHuia_PasswordlessFlowOnlyConfigured_EmailAndPasswordStaysDisabled()
    {
        var options = AddHuiaAndResolveOptions(huia => huia.Authentication.UsePasswordlessFlow());

        Assert.True(options.Authentication.PasswordlessFlowEnabled);
        Assert.False(options.Authentication.EmailAndPasswordFlowEnabled);
    }

    [Fact]
    public void AddHuia_BothFlowsExplicitlyConfigured_BothStayEnabled()
    {
        var options = AddHuiaAndResolveOptions(huia =>
        {
            huia.Authentication.UsePasswordlessFlow();
            huia.Authentication.UseEmailAndPasswordFlow();
        });

        Assert.True(options.Authentication.PasswordlessFlowEnabled);
        Assert.True(options.Authentication.EmailAndPasswordFlowEnabled);
    }

    /// <summary>
    /// The regression this guards: an even-older, removed <c>HuiaOptions.Identity</c> property mutated a
    /// disconnected <see cref="IdentityOptions"/> instance that <c>AddIdentityCore</c> never actually read
    /// from — changes silently never took effect. <c>huia.Identity</c>'s callback has to run against the
    /// exact same instance <c>AddIdentityCore</c> builds, provable only by resolving the real
    /// <see cref="IOptions{TOptions}"/> DI itself constructs, not by reading anything back off
    /// <see cref="HuiaOptions"/>.
    /// </summary>
    [Fact]
    public void AddHuia_IdentityConfigured_AppliesToTheRealResolvedIdentityOptions()
    {
        var services = new ServiceCollection();
        services.AddHuia("https://localhost", huia =>
        {
            huia.Authentication.UseEmailAndPasswordFlow();
            huia.Identity = identity => identity.Lockout.MaxFailedAccessAttempts = 7;
        });

        using var provider = services.BuildServiceProvider();
        var identityOptions = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(7, identityOptions.Lockout.MaxFailedAccessAttempts);
    }
}
