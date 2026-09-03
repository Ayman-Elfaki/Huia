using System.Net;
using System.Text.Json;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Huia.IntegrationTests;

/// <summary>
/// Exercises the real <c>Huia.IdentityServer</c> host against a PostgreSQL container — PostgreSQL is the
/// only tested provider, so this is where the schema DDL and query translation are proven.
/// </summary>
[Trait("Category", "Container")]
public sealed class PostgresProviderTests(ContainerHostFixture fixture) : IClassFixture<ContainerHostFixture>
{
    [Fact]
    public async Task The_schema_is_created_and_the_discovery_document_is_served()
    {
        var client = fixture.CreateClient();

        var discovery = await client.GetAsync("/todo/.well-known/openid-configuration");
        discovery.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("issuer").GetString().ShouldEndWith("/todo");
    }

    [Fact]
    public async Task A_client_credentials_token_is_signed_by_the_tenant_key_on_postgres()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsync("/e2e/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "e2e-worker",
            ["client_secret"] = "e2e-worker-secret",
        }));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var accessToken = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString();
        var jwt = new JsonWebToken(accessToken);
        jwt.Issuer.ShouldEndWith("/e2e");

        using var jwks = JsonDocument.Parse(await client.GetStringAsync("/e2e/.well-known/jwks"));
        var kid = jwks.RootElement.GetProperty("keys")[0].GetProperty("kid").GetString();
        jwt.Kid.ShouldBe(kid);
    }

    [Fact]
    public async Task The_key_ring_query_with_a_DateTimeOffset_ordering_runs_on_postgres()
    {
        var client = fixture.CreateClient();

        // GetPublishedJwksAsync orders the key rows by CreatedAt (a DateTimeOffset) — this is the query
        // that must not be forced client-side on Postgres.
        var jwks = await client.GetAsync("/todo/.well-known/jwks");
        jwks.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await jwks.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("keys").GetArrayLength().ShouldBeGreaterThan(0);
    }
}
