using System.Net;
using System.Net.Http.Json;

namespace Huia.Tests.Integration;

/// <summary>
/// Covers <c>MapHuiaManageEndpoints</c>'s auth policy: it accepts either the <c>Huia.Application</c>
/// cookie or a bearer access token (see <see cref="EndpointRouteBuilderExtensions"/>). This exercises the
/// bearer path specifically, since that's what a cross-origin OAuth client (the Todo App, the Admin UI) uses
/// — via the same <see cref="AdminTestHelpers"/> token-minting helper <see cref="AdminEndpointsTests"/> uses
/// — plus one cookie-path test as a regression guard for the pre-existing same-origin usage.
/// </summary>
public class ManageEndpointsTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string InfoPath = "/api/identity/manage/info";

    [Fact]
    public async Task GetInfo_WithoutCredentials_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync(InfoPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInfo_WithBearerToken_ReturnsOwnInfo()
    {
        var (client, email, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        using var response = await client.GetAsync(InfoPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Equal(email, info!.Email);
        Assert.Null(info.PhoneNumber);
        Assert.False(info.IsPhoneNumberConfirmed);
    }

    [Fact]
    public async Task UpdateInfo_WithBearerToken_PersistsChange()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);

        using var response = await client.PostAsJsonAsync(InfoPath, new
        {
            FirstName = "Updated",
            LastName = "Name",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Equal("Updated", info!.FirstName);
        Assert.Equal("Name", info.LastName);
    }

    [Theory]
    [InlineData("Invalid123", null)]
    [InlineData(null, "Invalid!")]
    public async Task UpdateInfo_WithInvalidName_ReturnsValidationProblemAndLeavesNameUnchanged(string? firstName,
        string? lastName)
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);
        var seeded = await client.PostAsJsonAsync(InfoPath, new { FirstName = "Original", LastName = "Name" });
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);

        var response = await client.PostAsJsonAsync(InfoPath, new { FirstName = firstName, LastName = lastName });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var fetched = await client.GetFromJsonAsync<InfoResponse>(InfoPath);
        Assert.Equal("Original", fetched!.FirstName);
        Assert.Equal("Name", fetched.LastName);
    }

    /// <summary>Omitting FirstName/LastName from the request leaves the existing value alone - only a field
    /// that's actually included must be a valid name (see ManageInfoEndpoints.UpdateInfoAsync).</summary>
    [Fact]
    public async Task UpdateInfo_OmittingName_LeavesExistingNameUnchanged()
    {
        var (client, _, _) = await AdminTestHelpers.CreateAuthorizedClientCoreAsync(factory, roles: []);
        var seeded = await client.PostAsJsonAsync(InfoPath, new { FirstName = "Kept", LastName = "Name" });
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);

        var response = await client.PostAsJsonAsync(InfoPath, new { NewEmail = (string?)null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Equal("Kept", info!.FirstName);
        Assert.Equal("Name", info.LastName);
    }

    /// <summary>Regression guard for the pre-existing same-origin (server-rendered) usage this endpoint group
    /// was originally built for, alongside the bearer path added for cross-origin OAuth clients above.</summary>
    [Fact]
    public async Task GetInfo_WithCookie_ReturnsOwnInfo()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var email = $"cookie-test-{Guid.NewGuid():N}@example.com";
        await IdentityUiTestHelpers.RegisterAsync(client, email, "P@ssw0rd123!");

        using var response = await client.GetAsync(InfoPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<InfoResponse>();
        Assert.Equal(email, info!.Email);
    }

    private sealed record InfoResponse(string? Email, bool IsEmailConfirmed, string? PhoneNumber,
        bool IsPhoneNumberConfirmed, string? FirstName, string? LastName);
}
