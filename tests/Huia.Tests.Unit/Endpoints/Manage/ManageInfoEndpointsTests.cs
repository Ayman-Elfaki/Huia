using Huia.Endpoints.Manage;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Unit.Endpoints.Manage;

/// <summary>
/// Covers <c>ManageInfoEndpoints.ValidatePasswordlessFlowEnabled</c> - the self-service phone-change
/// endpoints' rule that changing a phone number is only allowed when the app actually has passwordless phone
/// sign-in enabled (<c>huia.Authentication.UsePasswordlessFlow()</c>), the same gating
/// <c>UsersEndpointsTests</c> covers for the admin "create user by phone" endpoint.
/// </summary>
public class ManageInfoEndpointsTests
{
    private static HuiaOptions BuildOptions(Action<HuiaOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHuia("https://localhost", configure);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<HuiaOptions>();
    }

    [Fact]
    public void PasswordlessFlowEnabled_ReturnsNull()
    {
        var options = BuildOptions(huia => huia.Authentication.UsePasswordlessFlow());

        var errors = ManageInfoEndpoints.ValidatePasswordlessFlowEnabled(options, "NewPhoneNumber");

        Assert.Null(errors);
    }

    [Fact]
    public void PasswordlessFlowDisabled_ReturnsErrorKeyedByFieldName()
    {
        var options = BuildOptions(huia => huia.Authentication.UseEmailAndPasswordFlow());

        var errors = ManageInfoEndpoints.ValidatePasswordlessFlowEnabled(options, "NewPhoneNumber");

        Assert.NotNull(errors);
        Assert.Contains("NewPhoneNumber", errors!.Keys, StringComparer.Ordinal);
    }
}
