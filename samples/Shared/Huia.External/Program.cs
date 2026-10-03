using Huia.OpenId.Multitenancy;
using Huia.OpenId.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Huia.External.Tenants;

var builder = WebApplication.CreateBuilder(args);

var issuer = builder.Configuration.GetValue("Huia:Issuer", "https://localhost:5320");
var consumerBaseUrl = builder.Configuration.GetValue("Consumer:BaseUrl", "https://localhost:5310");
var shopConsumerBaseUrl = builder.Configuration.GetValue("ShopConsumer:BaseUrl", "https://localhost:5341");

var connection = new SqliteConnection(builder.Configuration.GetConnectionString("huia") ?? "DataSource=:memory:");
connection.Open();
builder.Services.AddSingleton(connection);
builder.Services.AddDbContext<HuiaDbContext>(options => options.UseSqlite(connection).UseOpenIddict());
builder.Services.AddHostedService<SchemaInitializer>();

builder.Services.AddHuiaOpenId(huia =>
    {
        huia.UseIssuer(issuer);
        huia.DisableTransportSecurityRequirement();
        huia.AddTenant(new PartnersTenant(consumerBaseUrl, shopConsumerBaseUrl));
    })
    .AddEntityFrameworkCoreStores<HuiaDbContext, HuiaUser, HuiaRole>()
    .AddHuiaUi();

builder.Services.AddHostedService<PartnerUserSeeder>();

var app = builder.Build();
app.UseHuiaOpenId();
app.MapHuiaEndpoints();
app.MapHuiaHome("partners");
app.Run();

/// <summary>Test entry point marker.</summary>
public partial class Program;

internal sealed class SchemaInitializer(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HuiaDbContext>().Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class PartnerUserSeeder(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, "partners"))
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            await EnsureAsync(userManager, "full@partners.test", "Partner1!Pass", "Fiona", "Full");
            await EnsureAsync(userManager, "partial@partners.test", "Partner1!Pass", string.Empty, string.Empty);
            // Same email as a downstream "todo" password user, to exercise auto-linking by confirmed email.
            await EnsureAsync(userManager, "link@partners.test", "Partner1!Pass", "Linus", "Existing");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureAsync(UserManager<HuiaUser> userManager, string email, string password,
        string first, string last)
    {
        if (await userManager.FindByNameAsync(email) is not null)
        {
            return;
        }

        await userManager.CreateAsync(new HuiaUser
        {
            TenantId = "partners",
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = first,
            LastName = last,
        }, password);
    }
}
