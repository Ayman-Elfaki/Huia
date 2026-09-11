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
        });
        return options;
    }

    [Fact]
    public void A_fully_populated_tree_validates()
    {
        Should.NotThrow(() => ValidOptions().Validate());
    }

    [Fact]
    public void PruneRemovedStaticEntities_defaults_to_false_and_either_value_validates()
    {
        var options = ValidOptions();
        options.Seeding.PruneRemovedStaticEntities.ShouldBeFalse();

        Should.NotThrow(() => options.Validate());

        options.Seeding.PruneRemovedStaticEntities = true;
        Should.NotThrow(() => options.Validate());
    }

    [Fact]
    public void Cleanup_defaults_validate_cleanly()
    {
        var options = ValidOptions();
        options.Cleanup.EnableBackgroundJobs.ShouldBeTrue();
        options.Cleanup.PruneAuthorizations.ShouldBeTrue();
        options.Cleanup.PruneTokens.ShouldBeTrue();
        options.Cleanup.MinimumAuthorizationLifespan.ShouldBe(TimeSpan.FromDays(14));
        options.Cleanup.MinimumTokenLifespan.ShouldBe(TimeSpan.FromDays(14));
        options.Cleanup.MaximumRefireCount.ShouldBe(2);

        Should.NotThrow(() => options.Validate());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_cleanup_lifespan_below_ten_minutes_is_rejected(bool setAuthorization, bool setToken)
    {
        var options = ValidOptions();
        if (setAuthorization)
        {
            options.Cleanup.MinimumAuthorizationLifespan = TimeSpan.FromMinutes(5);
        }

        if (setToken)
        {
            options.Cleanup.MinimumTokenLifespan = TimeSpan.FromMinutes(5);
        }

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("at least 10 minutes", StringComparison.Ordinal));
    }

    [Fact]
    public void A_negative_cleanup_maximum_refire_count_is_rejected()
    {
        var options = ValidOptions();
        options.Cleanup.MaximumRefireCount = -1;

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("must not be negative", StringComparison.Ordinal));
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
    public void AddRoles_declares_code_defined_roles()
    {
        var options = ValidOptions();
        options.Tenants["acme"].AddRoles("editor", "beta-tester");

        Should.NotThrow(() => options.Validate());
        options.Tenants["acme"].Roles.ShouldBe(["editor", "beta-tester"]);
    }

    [Fact]
    public void A_duplicate_role_name_is_rejected()
    {
        var options = ValidOptions();
        options.Tenants["acme"].AddRoles("editor", "editor");

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void An_invalid_role_name_is_rejected()
    {
        var options = ValidOptions();
        options.Tenants["acme"].AddRoles("has a space");

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("Roles[0]", StringComparison.Ordinal));
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
}
