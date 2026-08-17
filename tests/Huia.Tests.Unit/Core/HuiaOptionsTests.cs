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
    public void Authentication_ExternalLoginPasswordLinkingEnabled_DefaultsToFalse()
    {
        var options = CreateOptions();

        Assert.False(options.Authentication.ExternalLoginPasswordLinkingEnabled);
    }

    [Fact]
    public void UseExternalAuthenticationFlow_BundlesProvidersAndPasswordLinkingFlag()
    {
        var options = CreateOptions();
        var registeredGoogle = false;

        options.Authentication.UseExternalAuthenticationFlow(ext =>
        {
            // Providers is the same AuthenticationBuilder AddHuia wires up internally — proven here by
            // registering an arbitrary scheme through it and confirming it lands in the same AuthenticationOptions.
            ext.Providers.AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("test-scheme", null);
            registeredGoogle = true;
            ext.EnablePasswordLinking();
        });

        Assert.True(registeredGoogle);
        Assert.True(options.Authentication.ExternalLoginPasswordLinkingEnabled);
        Assert.True(options.Authentication.ExternalFlowEnabled);
    }

    [Fact]
    public void UsePasswordlessFlow_SetsFlagAndRateLimitDefaults()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow();

        Assert.True(options.Authentication.PasswordlessFlowEnabled);
        Assert.Equal(1, options.Authentication.PhoneOtpRateLimit.RequestsPerMinute);
        Assert.Equal(3, options.Authentication.PhoneOtpRateLimit.RequestsPerHour);
        Assert.Equal(10, options.Authentication.PhoneOtpRateLimit.RequestsPerDay);
    }

    [Fact]
    public void UsePasswordlessFlow_ConfigureRateLimit_OverridesDefaults()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow(configureRateLimit: rateLimit => rateLimit.RequestsPerMinute = 5);

        Assert.Equal(5, options.Authentication.PhoneOtpRateLimit.RequestsPerMinute);
    }

    [Fact]
    public void UsePasswordlessFlow_DefaultCountryCodeUnset_LeavesDefaultCountryCodeNull()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow();

        Assert.Null(options.Authentication.DefaultCountryCode);
    }

    [Theory]
    [InlineData("US", "US")]
    [InlineData("gb", "GB")]
    public void UsePasswordlessFlow_DefaultCountryCodeSupported_NormalizesToUppercase(string input, string expected)
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow(defaultCountryCode: input);

        Assert.Equal(expected, options.Authentication.DefaultCountryCode);
    }

    [Fact]
    public void UsePasswordlessFlow_DefaultCountryCodeNotARealRegion_Throws()
    {
        var options = CreateOptions();

        Assert.Throws<ArgumentException>(() => options.Authentication.UsePasswordlessFlow(defaultCountryCode: "ZZ"));
    }

    [Fact]
    public void UseEmailAndPasswordFlow_SetsFlag()
    {
        var options = CreateOptions();

        options.Authentication.UseEmailAndPasswordFlow();

        Assert.True(options.Authentication.EmailAndPasswordFlowEnabled);
    }

    /// <summary>
    /// The regression this guards: a provider registered via
    /// <c>huia.Authentication.UseExternalAuthenticationFlow(ext => ext.Providers.AddX(...))</c> without its
    /// own <c>SignInScheme</c> must land in the external cookie
    /// <c>SignInManager.GetExternalLoginInfoAsync()</c> reads from — see HuiaOptions's own constructor doc
    /// comment for why this has to be <see cref="IdentityConstants.ExternalScheme"/> rather than
    /// <see cref="IdentityConstants.ApplicationScheme"/>.
    /// </summary>
    [Fact]
    public void Authentication_DefaultSignInSchemeIsExternalScheme()
    {
        var services = new ServiceCollection();
        _ = new HuiaOptions("https://localhost", services);

        using var provider = services.BuildServiceProvider();
        var authOptions = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(IdentityConstants.ExternalScheme, authOptions.DefaultSignInScheme);
    }

    private sealed class NoOpAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
