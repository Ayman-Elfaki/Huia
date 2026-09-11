using Huia;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.OpenId;
using Huia.OpenId.EntityFrameworkCore;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var issuer = builder.Configuration.GetValue("Huia:Issuer", "https://localhost:5320")!;
var consumerBaseUrl = builder.Configuration.GetValue("Consumer:BaseUrl", "https://localhost:5310")!;

var connection = new SqliteConnection(builder.Configuration.GetConnectionString("huia") ?? "DataSource=:memory:");
connection.Open();
builder.Services.AddSingleton(connection);
builder.Services.AddDbContext<HuiaOpenIdDbContext>(options => options.UseSqlite(connection).UseOpenIddict());
builder.Services.AddHostedService<SchemaInitializer>();

var huiaBuilder = builder.Services.AddHuia(huia =>
{
    huia.UseIssuer(issuer);
    huia.DisableTransportSecurityRequirement();
    huia.AddTenant("partners", tenant =>
    {
        // The mock upstream IdP gets its own branding too (amber accent, legal pages served by the
        // main Huia sample it federates with).
        tenant.Branding.DisplayName = "Partner Directory";
        tenant.Branding.LogoUrl = $"{consumerBaseUrl}/brand/huia-logo.svg";
        tenant.Branding.FaviconUrl = $"{consumerBaseUrl}/brand/favicon.svg";
        tenant.Branding.AccentColor = "#d97706";
        tenant.Branding.TermsUrl = new Uri($"{consumerBaseUrl}/legal/terms.html");
        tenant.Branding.PrivacyUrl = new Uri($"{consumerBaseUrl}/legal/privacy.html");
        tenant.Branding.SupportUrl = new Uri("https://github.com/Ayman-Elfaki/Huia");
        tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
        tenant.AddHuiaOpenId(openId =>
        {
            openId.AddServerSideWebApplication("huia-idp", "huia-idp-secret", client =>
            {
                client.DisplayName = "Todo (via partner sign-in)";
                client.ClientUri = new Uri($"{consumerBaseUrl}/todo/");
                client.RedirectUris.Add(new Uri($"{consumerBaseUrl}/todo/signin-huiaexternal"));
                client.RedirectUris.Add(new Uri($"{consumerBaseUrl}/todo/signin-huiaexternalpartial"));
                // The downstream Huia's OpenIddict-client post-logout callback, so signing out of the Todo
                // app also ends this partner session.
                client.PostLogoutRedirectUris.Add(new Uri($"{consumerBaseUrl}/todo/signout-callback-oidc"));
                client.Scopes.Add("email");
                client.Scopes.Add("profile");
            });
        });
    });
}).AddHuiaOpenId();

huiaBuilder.AddHuiaUi();

builder.Services.AddHostedService<PartnerUserSeeder>();

var app = builder.Build();
app.UseHuiaOpenId();
app.MapHuiaOpenIdEndpoints();
app.Run();

/// <summary>Test entry point marker.</summary>
public partial class Program;

internal sealed class SchemaInitializer(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HuiaOpenIdDbContext>().Database.EnsureCreatedAsync(cancellationToken);
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

    private static async Task EnsureAsync(UserManager<HuiaUser> userManager, string email, string password, string first, string last)
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
