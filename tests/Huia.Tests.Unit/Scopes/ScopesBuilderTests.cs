using Huia.Scopes;

namespace Huia.Tests.Unit.Scopes;

public class ScopesBuilderTests
{
    [Fact]
    public void Add_WithoutName_Throws()
    {
        var builder = new ScopesBuilder();

        Assert.Throws<ArgumentException>(() => builder.Add(""));
    }

    [Fact]
    public void Add_ConfiguresDisplayNameDescriptionAndResources()
    {
        var builder = new ScopesBuilder();

        builder.Add("todos", scope =>
        {
            scope.SetDisplayName("Todos");
            scope.SetDescription("Read/write access to todos");
            scope.SetResource("todo-api", "todo-worker");
        });

        var scope = Assert.Single(builder.Scopes);
        Assert.Equal("todos", scope.Name);
        Assert.Equal("Todos", scope.DisplayName);
        Assert.Equal("Read/write access to todos", scope.Description);
        Assert.Equal(["todo-api", "todo-worker"], scope.Resources);
    }

    [Fact]
    public void Add_CalledTwiceForTheSameName_ReplacesTheEarlierDeclaration()
    {
        var builder = new ScopesBuilder();

        builder.Add("todos", scope => scope.SetResource("v1-api"));
        builder.Add("todos", scope => scope.SetResource("v2-api"));

        var scope = Assert.Single(builder.Scopes);
        Assert.Equal(["v2-api"], scope.Resources);
    }
}
