using Huia.Sessions;
using Xunit;

namespace Huia.Tests.Unit.Sessions;

public class UserSessionsBuilderTests
{
    [Fact]
    public void Options_DefaultsToFourteenDayAbsoluteLifetime()
    {
        var builder = new UserSessionsBuilder();

        Assert.Equal(TimeSpan.FromDays(14), builder.Options.AbsoluteLifetime);
    }

    [Fact]
    public void SetAbsoluteLifetime_UpdatesOptions_AndReturnsSameBuilder()
    {
        var builder = new UserSessionsBuilder();

        var result = builder.SetAbsoluteLifetime(TimeSpan.FromDays(30));

        Assert.Same(builder, result);
        Assert.Equal(TimeSpan.FromDays(30), builder.Options.AbsoluteLifetime);
    }
}
