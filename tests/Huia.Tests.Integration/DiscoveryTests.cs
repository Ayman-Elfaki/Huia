using System.Net;

namespace Huia.Tests.Integration;

public class DiscoveryTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    [Fact]
    public async Task DiscoveryDocument_IsReachable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ScalarApiReference_IsReachable_InDevelopment()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
