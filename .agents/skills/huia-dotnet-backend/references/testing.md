# Testing Applications Integrated with Huia

Guidance for writing automated integration and unit tests for applications consuming Huia.

---

## 1. Testing Resource Servers (APIs Protected by Huia)

When testing downstream APIs that use `OpenIddict.Validation.AspNetCore`:

### Strategy A: Test Authentication Scheme Override
In integration tests using `WebApplicationFactory<Program>`, bypass remote JWKS network calls by substituting a test authentication handler:

```csharp
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(defaultScheme: "TestScheme")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        "TestScheme", options => { });
            });
        }).CreateClient();
    }

    [Fact]
    public async Task GetOrders_WithAuthenticatedUser_ReturnsOk()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("TestScheme");

        var response = await _client.GetAsync("/api/orders");
        response.EnsureSuccessStatusCode();
    }
}
```

---

## 2. Testing the Identity Server Host

When testing your identity server host (`Huia.OpenId`):

### In-Memory SQLite Testing
Avoid provisioning an external database in unit/integration test suites by configuring SQLite in-memory mode:

```csharp
var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

builder.Services.AddDbContext<IdentityHuiaDbContext>(options =>
{
    options.UseSqlite(connection);
    options.UseOpenIddict();
});
```

### Discovery & JWKS Verification
Verify that tenant discovery endpoints respond properly:
```csharp
[Fact]
public async Task Tenant_OpenIdConfiguration_ReturnsValidJson()
{
    var response = await _client.GetAsync("/acme/.well-known/openid-configuration");
    response.EnsureSuccessStatusCode();

    var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
    Assert.Equal("https://id.example.com/acme", doc["issuer"]?.ToString());
    Assert.NotNull(doc["jwks_uri"]);
    Assert.NotNull(doc["authorization_endpoint"]);
    Assert.NotNull(doc["token_endpoint"]);
}
```
