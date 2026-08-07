using Huia.Applications;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Unit.Applications;

public class ApplicationsBuilderTests
{
    private static ApplicationsBuilder CreateBuilder() => new(new ServiceCollection());

    [Fact]
    public void AddSinglePageApplication_WithoutClientId_Throws()
    {
        var builder = CreateBuilder();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddSinglePageApplication(app => app.AllowScopes("todos")));

        Assert.Contains("client ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddServerSideWebApplication_WithoutClientSecret_Throws()
    {
        var builder = CreateBuilder();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddServerSideWebApplication(app => app.SetClientId("web")));

        Assert.Contains("client secret", exception.Message, StringComparison.Ordinal);
    }
    

    [Fact]
    public void AddMachine2Machine_WithClientIdAndSecret_Succeeds()
    {
        var builder = CreateBuilder();

        builder.AddMachine2Machine(app =>
        {
            app.SetClientId("worker");
            app.SetClientSecret("secret");
        });

        Assert.Single(builder.MachineToMachineApplications);
        Assert.Equal("worker", builder.MachineToMachineApplications[0].ClientId);
    }

    [Fact]
    public void GetAllScopes_ReturnsDistinctUnionAcrossApplications()
    {
        var builder = CreateBuilder();

        builder.AddSinglePageApplication(app =>
        {
            app.SetClientId("spa");
            app.AllowScopes("todos", "notes");
        });

        builder.AddMachine2Machine(app =>
        {
            app.SetClientId("worker");
            app.SetClientSecret("secret");
            app.AllowScopes("todos", "billing");
        });

        Assert.Equal(["todos", "notes", "billing"], builder.GetAllScopes());
    }

    [Fact]
    public void DisplayName_DefaultsToClientId_WhenUnset()
    {
        var builder = CreateBuilder();

        builder.AddNativeApplication(app => app.SetClientId("native-app"));

        Assert.Null(builder.NativeApplications[0].DisplayName);
        Assert.Equal("native-app", builder.NativeApplications[0].ClientId);
    }
}