using Huia.Options;

namespace Huia.Tests.Options;

public class PasskeyOptionsTests
{
    private static HuiaOptions OptionsWithPasskeyTenant(Action<PasskeyOptions>? configure = null)
    {
        var options = new HuiaOptions { Issuer = new Uri("https://id.example.test") };
        options.AddTenant("acme", tenant => tenant.Authentication.UsePasskeyLogin(configure));
        return options;
    }

    [Fact]
    public void UsePasskeyLogin_enables_the_flow_and_is_a_valid_sole_sign_in_method()
    {
        var options = OptionsWithPasskeyTenant();

        options.Tenants["acme"].Authentication.IsPasskeyLoginEnabled.ShouldBeTrue();
        Should.NotThrow(() => options.Validate());
    }

    [Fact]
    public void Passkey_policy_defaults_are_sensible()
    {
        var passkey = new PasskeyOptions();

        passkey.UserVerification.ShouldBe(PasskeyUserVerification.Required);
        passkey.AuthenticatorAttachment.ShouldBe(PasskeyAuthenticatorAttachment.Any);
        passkey.AuthenticatorTimeout.ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15 * 60)]
    public void An_authenticator_timeout_outside_30s_to_10m_is_rejected(int seconds)
    {
        var options = OptionsWithPasskeyTenant(p => p.AuthenticatorTimeout = TimeSpan.FromSeconds(seconds));

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("AuthenticatorTimeout", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tenant_with_only_passkey_login_satisfies_the_at_least_one_method_rule()
    {
        var options = new HuiaOptions { Issuer = new Uri("https://id.example.test") };
        options.AddTenant("acme", tenant => { });

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("at least one sign-in method", StringComparison.Ordinal));

        options.Tenants["acme"].Authentication.UsePasskeyLogin();
        Should.NotThrow(() => options.Validate());
    }

    [Theory]
    [InlineData("https://id.example.test")]
    [InlineData("id.example.test:5001")]
    [InlineData("id.example.test/path")]
    public void A_per_tenant_relying_party_id_that_is_not_a_bare_host_is_rejected(string relyingPartyId)
    {
        var options = OptionsWithPasskeyTenant(p => p.RelyingPartyId = relyingPartyId);

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("RelyingPartyId", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bare_host_relying_party_id_and_absolute_allowed_origins_validate()
    {
        var options = OptionsWithPasskeyTenant(p =>
        {
            p.RelyingPartyId = "acme.example.test";
            p.AllowedOrigins.Add("https://app.example.test");
        });

        Should.NotThrow(() => options.Validate());
    }

    [Fact]
    public void A_per_tenant_allowed_origin_with_a_path_is_rejected()
    {
        var options = OptionsWithPasskeyTenant(p => p.AllowedOrigins.Add("https://app.example.test/callback"));

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("AllowedOrigins", StringComparison.Ordinal));
    }
}
