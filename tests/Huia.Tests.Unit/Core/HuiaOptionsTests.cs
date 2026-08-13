using Huia.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Huia.Tests.Unit.Core;

public class HuiaOptionsTests
{
    private static HuiaOptions CreateOptions() => new("https://localhost", new ServiceCollection());

    [Fact]
    public void ExternalLoginPasswordLinkingEnabled_DefaultsToFalse()
    {
        var options = CreateOptions();

        Assert.False(options.ExternalLoginPasswordLinkingEnabled);
    }

    [Fact]
    public void EnableExternalLoginPasswordLinking_SetsFlag()
    {
        var options = CreateOptions();

        options.EnableExternalLoginPasswordLinking();

        Assert.True(options.ExternalLoginPasswordLinkingEnabled);
    }

    /// <summary>
    /// The regression this guards: a provider registered via <c>huia.ExternalLogins</c> without its own
    /// <c>SignInScheme</c> must land in the external cookie <c>SignInManager.GetExternalLoginInfoAsync()</c>
    /// reads from — see HuiaOptions's own constructor doc comment for why this has to be
    /// <see cref="IdentityConstants.ExternalScheme"/> rather than <see cref="IdentityConstants.ApplicationScheme"/>.
    /// </summary>
    [Fact]
    public void ExternalLogins_DefaultSignInSchemeIsExternalScheme()
    {
        var services = new ServiceCollection();
        _ = new HuiaOptions("https://localhost", services);

        using var provider = services.BuildServiceProvider();
        var authOptions = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(IdentityConstants.ExternalScheme, authOptions.DefaultSignInScheme);
    }
}
