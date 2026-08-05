using System.Text.Json;

namespace Huia.Tests.Integration;

internal static class OAuthTestHelpers
{
    /// <summary>Requests a client_credentials token from the "todo-tests" M2M client registered by the sample.</summary>
    public static async Task<string> GetTodoTestsAccessTokenAsync(HttpClient client, string scope = "todos")
    {
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "todo-tests",
            ["client_secret"] = "todo-tests-dev-secret",
            ["scope"] = scope,
        }));

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("access_token").GetString()!;
    }
}