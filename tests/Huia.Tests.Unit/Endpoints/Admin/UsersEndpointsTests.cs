using Huia.Endpoints.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Unit.Endpoints.Admin;

/// <summary>
/// Covers <c>UsersEndpoints.ValidateIdentifierChoice</c> - the admin "create user" endpoint's rule that
/// creating an account by email or by phone number is only allowed when the app actually has the matching
/// sign-in flow enabled (<c>huia.Authentication.Use...Flow()</c>), so an admin can never provision an account
/// with no way to sign back in.
/// </summary>
public class UsersEndpointsTests
{
    private static HuiaOptions BuildOptions(Action<HuiaOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHuia("https://localhost", configure);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<HuiaOptions>();
    }

    [Fact]
    public void NeitherEmailNorPhoneSupplied_ReturnsError()
    {
        var options = BuildOptions(huia => huia.Authentication.UseEmailAndPasswordFlow());

        var errors = UsersEndpoints.ValidateIdentifierChoice(hasEmail: false, hasPhone: false, options);

        Assert.NotNull(errors);
    }

    [Fact]
    public void BothEmailAndPhoneSupplied_ReturnsError()
    {
        var options = BuildOptions(huia =>
        {
            huia.Authentication.UseEmailAndPasswordFlow();
            huia.Authentication.UsePasswordlessFlow();
        });

        var errors = UsersEndpoints.ValidateIdentifierChoice(hasEmail: true, hasPhone: true, options);

        Assert.NotNull(errors);
    }

    [Fact]
    public void PhoneSupplied_PasswordlessFlowEnabled_ReturnsNull()
    {
        var options = BuildOptions(huia => huia.Authentication.UsePasswordlessFlow());

        var errors = UsersEndpoints.ValidateIdentifierChoice(hasEmail: false, hasPhone: true, options);

        Assert.Null(errors);
    }

    [Fact]
    public void PhoneSupplied_PasswordlessFlowDisabled_ReturnsError()
    {
        var options = BuildOptions(huia => huia.Authentication.UseEmailAndPasswordFlow());

        var errors = UsersEndpoints.ValidateIdentifierChoice(hasEmail: false, hasPhone: true, options);

        Assert.NotNull(errors);
        Assert.Contains("PhoneNumber", errors!.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public void EmailSupplied_EmailAndPasswordFlowEnabled_ReturnsNull()
    {
        var options = BuildOptions(huia => huia.Authentication.UseEmailAndPasswordFlow());

        var errors = UsersEndpoints.ValidateIdentifierChoice(hasEmail: true, hasPhone: false, options);

        Assert.Null(errors);
    }

    [Fact]
    public void EmailSupplied_EmailAndPasswordFlowDisabled_ReturnsError()
    {
        var options = BuildOptions(huia => huia.Authentication.UsePasswordlessFlow());

        var errors = UsersEndpoints.ValidateIdentifierChoice(hasEmail: true, hasPhone: false, options);

        Assert.NotNull(errors);
        Assert.Contains("Email", errors!.Keys, StringComparer.Ordinal);
    }
}
