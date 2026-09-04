using Huia.Options;

namespace Huia.Tests.Options;

public class HuiaOptionsValidationTests
{
    private static HuiaOptions ValidOptions()
    {
        var options = new HuiaOptions { Issuer = new Uri("https://id.example.test") };
        options.AddTenant("acme", tenant =>
        {
            tenant.Authentication.UseEmailAndPasswordLogin();
            tenant.AddClient("acme-web", ClientKind.ServerSideWebApplication);
            var client = tenant.Clients[0];
            client.ClientSecret = "s3cret-value";
            client.RedirectUris.Add(new Uri("https://acme.example.test/callback"));
        });
        return options;
    }

    [Fact]
    public void A_fully_populated_tree_validates()
    {
        Should.NotThrow(() => ValidOptions().Validate());
    }

    [Fact]
    public void Issuer_is_required()
    {
        var options = new HuiaOptions();
        options.AddTenant("acme", t => t.Authentication.UseEmailAndPasswordLogin());

        var ex = Should.Throw<HuiaOptionsException>(() => options.Validate());
        ex.Errors.ShouldContain(e => e.Contains("Issuer", StringComparison.Ordinal));
    }

    [Fact]
    public void Http_issuer_is_rejected_unless_transport_security_is_disabled()
    {
        var options = ValidOptions();
        options.Issuer = new Uri("http://id.example.test");

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("HTTPS", StringComparison.Ordinal));

        options.DisableTransportSecurityRequirement = true;
        Should.NotThrow(() => options.Validate());
    }

    [Fact]
    public void At_least_one_tenant_is_required()
    {
        var options = new HuiaOptions { Issuer = new Uri("https://id.example.test") };

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_tenant_with_no_sign_in_method_is_rejected()
    {
        var options = new HuiaOptions { Issuer = new Uri("https://id.example.test") };
        options.AddTenant("acme", _ => { });

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("sign-in method", StringComparison.Ordinal));
    }

    [Fact]
    public void Email_and_password_login_is_off_until_UseEmailAndPasswordLogin_is_called()
    {
        var tenant = new TenantOptions();
        tenant.Authentication.EmailAndPassword.Enabled.ShouldBeFalse();

        tenant.Authentication.UseEmailAndPasswordLogin(password => password.MinimumLength = 12);

        tenant.Authentication.EmailAndPassword.Enabled.ShouldBeTrue();
        tenant.Authentication.EmailAndPassword.MinimumLength.ShouldBe(12);
    }

    [Fact]
    public void Self_service_registration_is_on_by_default_and_DisableRegistration_turns_it_off()
    {
        var tenant = new TenantOptions();
        tenant.Authentication.EmailAndPassword.AllowSelfServiceRegistration.ShouldBeTrue();

        tenant.DisableRegistration();

        tenant.Authentication.EmailAndPassword.AllowSelfServiceRegistration.ShouldBeFalse();
    }

    [Fact]
    public void DisableRegistration_does_not_enable_the_phone_flow_when_it_was_never_used()
    {
        var tenant = new TenantOptions();

        tenant.DisableRegistration();

        tenant.Authentication.IsPhoneLoginEnabled.ShouldBeFalse();
    }

    [Fact]
    public void DisableRegistration_also_turns_off_phone_auto_provisioning_when_the_phone_flow_is_enabled()
    {
        var tenant = new TenantOptions();
        tenant.Authentication.UsePhoneLogin(phone => phone.AllowAutoProvisioning = true);

        tenant.DisableRegistration();

        tenant.Authentication.EmailAndPassword.AllowSelfServiceRegistration.ShouldBeFalse();
        tenant.Authentication.Phone!.AllowAutoProvisioning.ShouldBeFalse();
    }

    [Fact]
    public void A_confidential_client_without_a_secret_is_rejected()
    {
        var options = ValidOptions();
        options.Tenants["acme"].Clients[0].ClientSecret = null;

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("confidential client", StringComparison.Ordinal));
    }

    [Fact]
    public void A_public_client_with_a_secret_is_rejected()
    {
        var options = ValidOptions();
        options.Tenants["acme"].AddClient("acme-spa", ClientKind.SinglePageApplication);
        var spa = options.Tenants["acme"].Clients[1];
        spa.ClientSecret = "should-not-be-here";
        spa.RedirectUris.Add(new Uri("https://acme.example.test/spa"));

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("public", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_client_ids_within_a_tenant_are_rejected()
    {
        var options = ValidOptions();
        var dup = options.Tenants["acme"].AddClient("acme-web", ClientKind.MachineToMachine);
        dup.ClientSecret = "another-secret";

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("more than once", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("acme", true)]
    [InlineData("acme-corp", true)]
    [InlineData("t1", true)]
    [InlineData("Acme", false)]
    [InlineData("-acme", false)]
    [InlineData("acme-", false)]
    [InlineData("acme_corp", false)]
    [InlineData("", false)]
    public void Tenant_identifier_rules(string candidate, bool expected)
    {
        HuiaOptions.IsValidTenantId(candidate).ShouldBe(expected);
    }

    [Fact]
    public void An_invalid_default_country_is_rejected()
    {
        var options = ValidOptions();
        options.Tenants["acme"].Authentication.UsePhoneLogin(phone => phone.DefaultCountry = "usa");

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("DefaultCountry", StringComparison.Ordinal));
    }

    [Fact]
    public void External_provider_names_must_be_unique()
    {
        var options = ValidOptions();
        options.Tenants["acme"].Authentication.UseExternalLogin(ext =>
        {
            ext.AddOpenIdConnect("Partner", "id-1", "secret-1", "https://partner-a.example.test");
            ext.AddOpenIdConnect("Partner", "id-2", "secret-2", "https://partner-b.example.test");
        });

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("duplicated", StringComparison.Ordinal));
    }
}
