using Huia.Options;

namespace Huia.Tests.Options;

public class OptionsMergeTests
{
    [Fact]
    public void Email_scalar_fields_fall_back_to_the_root()
    {
        var root = new EmailOptions
        {
            Host = "smtp.root.test",
            Port = 2525,
            UseSsl = false,
            FromAddress = "noreply@root.test",
            FromName = "Root",
            UserName = "root-user",
            Password = "root-pass",
        };

        var effective = root.MergedWith(new EmailOptions { FromName = "Tenant" });

        effective.Host.ShouldBe("smtp.root.test");
        effective.Port.ShouldBe(2525);
        effective.UseSsl.ShouldBeFalse();
        effective.FromAddress.ShouldBe("noreply@root.test");
        effective.FromName.ShouldBe("Tenant");
    }

    [Fact]
    public void Email_port_and_ssl_are_not_inherited_when_the_tenant_sets_its_own_host()
    {
        var root = new EmailOptions { Host = "smtp.root.test", Port = 2525, UseSsl = false, FromAddress = "a@root.test" };
        var tenant = new EmailOptions { Host = "smtp.tenant.test", FromAddress = "a@tenant.test" };

        var effective = root.MergedWith(tenant);

        effective.Host.ShouldBe("smtp.tenant.test");
        effective.Port.ShouldBe(587);   // the tenant's own (default) transport, not the root's 2525
        effective.UseSsl.ShouldBeTrue();
    }

    [Fact]
    public void Sms_rate_limit_is_taken_wholesale_from_the_root_unless_the_tenant_sets_a_provider()
    {
        var root = new SmsOptions
        {
            Provider = "twilio",
            ApiKey = "root-key",
            RateLimit = new OtpRateLimitOptions { PermitLimit = 9, MaxPerNumberPerDay = 99 },
        };

        // Tenant tweaks the limit but does NOT set its own provider -> the tweak is discarded.
        var withoutProvider = root.MergedWith(new SmsOptions
        {
            RateLimit = new OtpRateLimitOptions { PermitLimit = 1, MaxPerNumberPerDay = 1 },
        });
        withoutProvider.RateLimit.PermitLimit.ShouldBe(9);

        // Tenant sets its own provider -> its limit instance is kept.
        var withProvider = root.MergedWith(new SmsOptions
        {
            Provider = "vonage",
            RateLimit = new OtpRateLimitOptions { PermitLimit = 1, MaxPerNumberPerDay = 5 },
        });
        withProvider.Provider.ShouldBe("vonage");
        withProvider.RateLimit.PermitLimit.ShouldBe(1);
    }

    [Fact]
    public void Sms_log_codes_flag_is_or_merged()
    {
        var root = new SmsOptions { Provider = "twilio", ApiKey = "k", LogCodesToLogger = false };
        root.MergedWith(new SmsOptions { LogCodesToLogger = true }).LogCodesToLogger.ShouldBeTrue();

        var rootOn = new SmsOptions { Provider = "twilio", ApiKey = "k", LogCodesToLogger = true };
        rootOn.MergedWith(new SmsOptions()).LogCodesToLogger.ShouldBeTrue();
    }

    [Fact]
    public void Merging_with_null_returns_an_independent_copy()
    {
        var root = new EmailOptions { Host = "smtp.root.test", FromAddress = "a@root.test" };
        var copy = root.MergedWith(null);

        copy.ShouldNotBeSameAs(root);
        copy.Host.ShouldBe("smtp.root.test");
    }
}
