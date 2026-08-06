using Huia.Sessions;
using Xunit;

namespace Huia.Tests.Unit.Sessions;

public class UserSessionsBuilderTests
{
    [Fact]
    public void Options_DefaultsToTenHourAbsoluteLifetime()
    {
        var builder = new UserSessionsBuilder();

        // Keycloak's "SSO Session Max" default.
        Assert.Equal(TimeSpan.FromHours(10), builder.Options.AbsoluteLifetime);
    }

    [Fact]
    public void Options_DefaultsToThirtyMinuteIdleTimeout()
    {
        var builder = new UserSessionsBuilder();

        // Keycloak's "SSO Session Idle" default.
        Assert.Equal(TimeSpan.FromMinutes(30), builder.Options.IdleTimeout);
    }

    [Fact]
    public void SetAbsoluteLifetime_UpdatesOptions_AndReturnsSameBuilder()
    {
        var builder = new UserSessionsBuilder();

        var result = builder.SetAbsoluteLifetime(TimeSpan.FromDays(30));

        Assert.Same(builder, result);
        Assert.Equal(TimeSpan.FromDays(30), builder.Options.AbsoluteLifetime);
    }

    [Fact]
    public void SetIdleTimeout_UpdatesOptions_AndReturnsSameBuilder()
    {
        var builder = new UserSessionsBuilder();

        var result = builder.SetIdleTimeout(TimeSpan.FromMinutes(15));

        Assert.Same(builder, result);
        Assert.Equal(TimeSpan.FromMinutes(15), builder.Options.IdleTimeout);
    }
}
