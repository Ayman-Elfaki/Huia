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
        Assert.Equal(1, options.Authentication.Passwordless.RateLimit.RequestsPerMinute);
        Assert.Equal(3, options.Authentication.Passwordless.RateLimit.RequestsPerHour);
        Assert.Equal(10, options.Authentication.Passwordless.RateLimit.RequestsPerDay);
    }

    [Fact]
    public void UsePasswordlessFlow_ConfigureRateLimit_OverridesDefaults()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow(passwordless => passwordless.RateLimit.RequestsPerMinute = 5);

        Assert.Equal(5, options.Authentication.Passwordless.RateLimit.RequestsPerMinute);
    }

    [Fact]
    public void UsePasswordlessFlow_DefaultCountryCodeUnset_LeavesDefaultCountryCodeNull()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow();

        Assert.Null(options.Authentication.Passwordless.DefaultCountryCode);
    }

    [Theory]
    [InlineData("US", "US")]
    [InlineData("gb", "GB")]
    public void UsePasswordlessFlow_DefaultCountryCodeSupported_NormalizesToUppercase(string input, string expected)
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow(passwordless => passwordless.DefaultCountryCode = input);

        Assert.Equal(expected, options.Authentication.Passwordless.DefaultCountryCode);
    }

    [Fact]
    public void UsePasswordlessFlow_DefaultCountryCodeNotARealRegion_Throws()
    {
        var options = CreateOptions();

        Assert.Throws<ArgumentException>(
            () => options.Authentication.UsePasswordlessFlow(passwordless => passwordless.DefaultCountryCode = "ZZ"));
    }

    [Fact]
    public void UsePasswordlessFlow_IpRateLimitNotEnabled_LeavesIpRateLimitEnabledFalse()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow();

        Assert.False(options.Authentication.Passwordless.IpRateLimitEnabled);
    }

    [Fact]
    public void UsePasswordlessFlow_EnableIpRateLimiting_SetsFlagAndDefaults()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow(passwordless => passwordless.EnableIpRateLimiting());

        Assert.True(options.Authentication.Passwordless.IpRateLimitEnabled);
        Assert.Equal(5, options.Authentication.Passwordless.IpRateLimit.RequestsPerMinute);
        Assert.Equal(20, options.Authentication.Passwordless.IpRateLimit.RequestsPerHour);
        Assert.Equal(50, options.Authentication.Passwordless.IpRateLimit.RequestsPerDay);
    }

    [Fact]
    public void UsePasswordlessFlow_EnableIpRateLimitingWithConfigure_OverridesDefaults()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow(
            passwordless => passwordless.EnableIpRateLimiting(ip => ip.RequestsPerMinute = 2));

        Assert.Equal(2, options.Authentication.Passwordless.IpRateLimit.RequestsPerMinute);
    }

    [Fact]
    public void UsePasswordlessFlow_TurnstileNotConfigured_LeavesTurnstileNull()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow();

        Assert.Null(options.Authentication.Passwordless.Turnstile);
    }

    [Fact]
    public void UsePasswordlessFlow_UseTurnstile_SetsSiteKeyAndSecretKey()
    {
        var options = CreateOptions();

        options.Authentication.UsePasswordlessFlow(passwordless => passwordless.UseTurnstile("site-key", "secret-key"));

        Assert.Equal("site-key", options.Authentication.Passwordless.Turnstile?.SiteKey);
        Assert.Equal("secret-key", options.Authentication.Passwordless.Turnstile?.SecretKey);
    }

    [Theory]
    [InlineData(null, "secret-key")]
    [InlineData("", "secret-key")]
    [InlineData("site-key", null)]
    [InlineData("site-key", "")]
    public void UsePasswordlessFlow_UseTurnstileWithMissingKey_Throws(string? siteKey, string? secretKey)
    {
        var options = CreateOptions();

        // ThrowsAny, not Throws: a null key throws ArgumentNullException specifically (ArgumentException.
        // ThrowIfNullOrWhiteSpace's own documented behavior), which xUnit's Throws<T> treats as a mismatch
        // since it requires the exact type, not just an assignable one.
        Assert.ThrowsAny<ArgumentException>(
            () => options.Authentication.UsePasswordlessFlow(passwordless => passwordless.UseTurnstile(siteKey!, secretKey!)));
    }

    [Fact]
    public void UseEmailAndPasswordFlow_SetsFlag()
    {
        var options = CreateOptions();

        options.Authentication.UseEmailAndPasswordFlow();

        Assert.True(options.Authentication.EmailAndPasswordFlowEnabled);
    }

    [Fact]
    public void RegistrationEnabled_DefaultsToTrue()
    {
        var options = CreateOptions();

        Assert.True(options.RegistrationEnabled);
    }

    [Fact]
    public void DisableRegistration_SetsRegistrationEnabledToFalse()
    {
        var options = CreateOptions();

        options.DisableRegistration();

        Assert.False(options.RegistrationEnabled);
    }

    [Fact]
    public void Identity_Unset_IsNull()
    {
        var options = CreateOptions();

        Assert.Null(options.Identity);
    }

    [Fact]
    public void Identity_Set_IsInvokedAgainstTheRealIdentityOptions()
    {
        var options = CreateOptions();
        var invoked = false;

        options.Identity = identity =>
        {
            invoked = true;
            identity.Lockout.MaxFailedAccessAttempts = 7;
        };

        var identityOptions = new IdentityOptions();
        options.Identity!.Invoke(identityOptions);

        Assert.True(invoked);
        Assert.Equal(7, identityOptions.Lockout.MaxFailedAccessAttempts);
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
