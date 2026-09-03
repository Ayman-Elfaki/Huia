using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Huia.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real <c>Huia.IdentityServer</c> host against a throw-away PostgreSQL container — PostgreSQL
/// is the only tested provider, so this is where the schema and query translation are exercised for real.
/// </summary>
public sealed class ContainerHostFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("huia")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Server; // force host creation now
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Huia:Database"] = "Postgres",
            ["Huia:Issuer"] = "https://identity.huia.test",
            ["Huia:EnableE2E"] = "true",
            ["Huia:EnableBackgroundJobs"] = "false",
            ["ConnectionStrings:huia"] = _postgres.GetConnectionString(),
        }));

        return base.CreateHost(builder);
    }
}
