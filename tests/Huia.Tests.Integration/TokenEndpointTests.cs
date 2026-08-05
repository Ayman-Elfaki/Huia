using System.Net;

namespace Huia.Tests.Integration;


public class TokenEndpointTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    [Fact]
    public async Task ClientCredentials_WithRegisteredScope_IssuesToken()
    {
        var client = factory.CreateClient();

        var token = await OAuthTestHelpers.GetTodoTestsAccessTokenAsync(client);

        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public async Task ClientCredentials_WithUnregisteredScope_Fails()
    {
        var client = factory.CreateClient();

        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "todo-tests",
            ["client_secret"] = "todo-tests-dev-secret",
            ["scope"] = "not-a-real-scope",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClientCredentials_WithWrongSecret_Fails()
    {
        var client = factory.CreateClient();

        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "todo-tests",
            ["client_secret"] = "wrong-secret",
            ["scope"] = "todos",
        }));

        // RFC 6749 section 5.2: invalid_client for a confidential client is reported as 401, not 400.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
