using Huia;
using Huia.EntityFrameworkCore;
using Huia.IdentityServer;
using Huia.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databaseProvider = builder.Configuration.GetValue("Huia:Database", "Sqlite")!;
var enableE2E = builder.Configuration.GetValue("Huia:EnableE2E", false);
var issuer = builder.Configuration.GetValue("Huia:Issuer", "https://localhost:5310")!;
var todoAppUrl = builder.Configuration.GetValue("Clients:TodoApp:BaseUrl", "http://localhost:3000")!;
var adminAppUrl = builder.Configuration.GetValue("Clients:AdminApp:BaseUrl", "http://localhost:3001")!;
// The huia-nuxt module's own E2E playground (EnableE2E only).
var playgroundUrl = builder.Configuration.GetValue("Clients:PlaygroundApp:BaseUrl", "http://localhost:3030")!;
// Huia.External is the mock upstream IdP the "todo" tenant's external-login button federates to.
var externalIssuer = builder.Configuration.GetValue("Huia:ExternalIssuer", "https://localhost:5320")!;

// A single shared in-memory SQLite connection kept open for the process lifetime.
SqliteConnection? sqliteConnection = null;
if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("huia") ?? "DataSource=:memory:";
    sqliteConnection = new SqliteConnection(connectionString);
    sqliteConnection.Open();
    builder.Services.AddSingleton(sqliteConnection);
    builder.Services.AddDbContext<HuiaDbContext>(options => options.UseSqlite(sqliteConnection).UseOpenIddict());
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("huia")
        ?? throw new InvalidOperationException("A 'huia' connection string is required for the Postgres provider.");
    builder.Services.AddDbContext<HuiaDbContext>(options => options.UseNpgsql(connectionString).UseOpenIddict());
}

// The schema initializer runs before any Huia hosted service (registration order).
builder.Services.AddHostedService<SchemaInitializer>();

// True once an SMTP host is configured (via Aspire's Mailpit wiring, or appsettings). When set, the real
// MailKit sender is used and the in-memory CapturingEmailSender / /e2e-mail fallback is left out.
var smtpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Huia:Email:Host"]);

var huiaBuilder = builder.Services.AddHuia(huia =>
{
    huia.UseIssuer(issuer);
    huia.UsePublicUrl(issuer);
    if (builder.Environment.IsDevelopment() || enableE2E)
    {
        huia.DisableTransportSecurityRequirement();
    }

    huia.ConfigureEmail(email => builder.Configuration.GetSection("Huia:Email").Bind(email));
    huia.ConfigureSms(sms => sms.LogCodesToLogger = builder.Environment.IsDevelopment());
    // The relying-party id defaults to the request host, which is right for this single-host sample.
    huia.ConfigurePasskeys(passkey => passkey.RelyingPartyName = "Huia");
    huia.ConfigureKeys(keys => keys.EnableBackgroundJobs = builder.Configuration.GetValue("Huia:EnableBackgroundJobs", true));
    huia.ConfigureCleanup(cleanup => cleanup.EnableBackgroundJobs = builder.Configuration.GetValue("Huia:EnableBackgroundJobs", true));

    huia.AddTenant("master", tenant =>
    {
        // Every branding option, so the account UI shows the logo, favicon, accent and legal footer.
        tenant.Branding.DisplayName = "Huia Admin";
        tenant.Branding.LogoUrl = "/brand/huia-logo.svg";
        tenant.Branding.FaviconUrl = "/brand/favicon.svg";
        tenant.Branding.AccentColor = "#4f46e5";
        tenant.Branding.TermsUrl = new Uri($"{issuer}/legal/terms.html");
        tenant.Branding.PrivacyUrl = new Uri($"{issuer}/legal/privacy.html");
        tenant.Branding.SupportUrl = new Uri("https://github.com/Ayman-Elfaki/Huia");
        tenant.Authentication.UseEmailAndPasswordLogin(password =>
        {
            password.RequireConfirmedEmail = false;

            // Administrators fat-finger their password more than they get brute-forced; be lenient.
            password.MaxFailedAccessAttempts = 10;
        });
        tenant.DisableRegistration();

        tenant.AddServerSideWebApplication("huia-admin-ui", "huia-admin-ui-secret", client =>
        {
            client.DisplayName = "Huia Admin Console";
            client.ClientUri = new Uri($"{adminAppUrl}/");
            client.LogoUri = new Uri($"{issuer}/brand/huia-logo.svg");
            client.RedirectUris.Add(new Uri($"{adminAppUrl}/auth/oidc/callback"));
            client.PostLogoutRedirectUris.Add(new Uri($"{adminAppUrl}/"));
            client.HomeUris.Add(new Uri($"{adminAppUrl}/"));
            client.RequirePushedAuthorizationRequests();
        });

        // The Huia.Cli admin tool signs in here with the device-authorization grant.
        tenant.AddDevice("huia-cli", client =>
        {
            client.ClientSecret = "huia-cli-secret";
            client.Token.DeviceCode = TimeSpan.FromMinutes(10);
            client.Token.UserCode = TimeSpan.FromMinutes(10);
        });
    });

    huia.AddTenant("todo", tenant =>
    {
        // A distinct accent from the master tenant, to show per-tenant theming of the account UI.
        tenant.Branding.DisplayName = "Todo";
        tenant.Branding.LogoUrl = "/brand/huia-logo.svg";
        tenant.Branding.FaviconUrl = "/brand/favicon.svg";
        tenant.Branding.AccentColor = "#059669";
        tenant.Branding.TermsUrl = new Uri($"{issuer}/legal/terms.html");
        tenant.Branding.PrivacyUrl = new Uri($"{issuer}/legal/privacy.html");
        tenant.Branding.SupportUrl = new Uri("https://github.com/Ayman-Elfaki/Huia");
        // A stricter password policy than the master tenant, to show the per-tenant IdentityOptions.
        tenant.Authentication.UseEmailAndPasswordLogin(password =>
        {
            password.MinimumLength = 12;
            password.RequireNonAlphanumeric = true;
            password.RequireConfirmedEmail = false;
            password.MaxFailedAccessAttempts = 3;
            password.LockoutDuration = TimeSpan.FromMinutes(30);
        });

        // A code-defined ("static") scope: the admin console shows it but will not let you edit or
        // delete it — those actions are reserved for scopes created through the admin API.
        tenant.AddScope("reports:read", scope =>
        {
            scope.DisplayName = "Read reports";
            scope.Description = "Read-only access to the reporting API.";
            scope.Resources.Add("reports-api");
        });

        // Code-defined ("static") roles: created at start-up if missing, and read-only in the admin
        // console like scopes/clients. Set Huia:Seeding:PruneRemovedStaticEntities to also delete one
        // that's no longer declared here.
        tenant.AddRoles("editor", "beta-tester");

        // Passkeys: a discoverable one-tap sign-in and the option to require a passkey as a second
        // factor after the password.
        tenant.Authentication.UsePasskeyLogin();

        tenant.Authentication.UsePhoneLogin(phone =>
        {
            phone.DefaultCountry = "SA";
            phone.AllowAutoProvisioning = true;

            // Throttle *successful* phone sign-ins per number: at most once every two minutes and
            // five times a day. Both knobs live on PhoneOptions and are configurable per tenant.
            phone.SuccessfulLoginsPerWindow = 1;
            phone.SuccessfulLoginWindow = TimeSpan.FromMinutes(2);
            phone.SuccessfulLoginsPerDay = 5;

            // The phone flow's own lockout ceiling — independent of the password flow's above.
            phone.MaxFailedAccessAttempts = 5;
        });

        tenant.Authentication.UseExternalLogin(ext =>
        {
            ext.AddOpenIdConnect(
                "HuiaExternal", "huia-idp", "huia-idp-secret", $"{externalIssuer}/partners", p =>
                {
                    p.DisplayName = "Partner";
                    p.Scopes.Add("profile");
                    p.Scopes.Add("email");
                });

            // An external sign-in whose (verified) email matches an existing confirmed local
            // account is linked to it instead of starting a new sign-up.
            ext.EnableAccountsLinking();
        });

        tenant.AddServerSideWebApplication("todo-app", "todo-app-secret", client =>
        {
            client.DisplayName = "Todo";
            client.ClientUri = new Uri($"{todoAppUrl}/");
            client.LogoUri = new Uri($"{issuer}/brand/huia-logo.svg");
            client.RedirectUris.Add(new Uri($"{todoAppUrl}/auth/oidc/callback"));
            client.PostLogoutRedirectUris.Add(new Uri($"{todoAppUrl}/"));
            client.HomeUris.Add(new Uri($"{todoAppUrl}/"));
        });
        
    });

    if (enableE2E)
    {
        huia.AddTenant("e2e", tenant =>
        {
            tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
            tenant.Authentication.UsePasskeyLogin(passkey => passkey.UserVerification = PasskeyUserVerification.Preferred);
            tenant.Authentication.UsePhoneLogin(p =>
            {
                p.AllowAutoProvisioning = true;

                // The E2E stack reuses a handful of numbers across specs on one long-lived host, so keep
                // the successful-sign-in throttle out of the way; the limiter has its own unit coverage.
                p.SuccessfulLoginsPerWindow = 100;
                p.SuccessfulLoginsPerDay = 1000;
            });
            tenant.AddSinglePageApplication("e2e-spa", client =>
                client.RedirectUris.Add(new Uri($"{issuer}/e2e/e2e-callback")));
            tenant.AddMachineToMachineApplication("e2e-worker", "e2e-worker-secret");

            // Confidential web client for the huia-nuxt playground. A short access-token
            // lifetime so the module's transparent refresh is exercised on the next request.
            tenant.AddServerSideWebApplication("huia-nuxt-playground", "huia-nuxt-playground-secret", client =>
            {
                client.DisplayName = "huia-nuxt playground";
                client.RedirectUris.Add(new Uri($"{playgroundUrl}/auth/oidc/callback"));
                client.PostLogoutRedirectUris.Add(new Uri($"{playgroundUrl}/"));
                client.HomeUris.Add(new Uri($"{playgroundUrl}/"));
                client.Token.AccessToken = TimeSpan.FromSeconds(35);
                client.Token.RefreshToken = TimeSpan.FromMinutes(30);
            });
        });

        // Self-service registration (on by default) with mandatory email confirmation, for the
        // confirm-email E2E spec.
        huia.AddTenant("e2e-signup", tenant =>
        {
            tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = true);
        });
    }
});

huiaBuilder.AddHuiaUi();
huiaBuilder.AddHuiaSecurityHeaders();

builder.Services.AddSingleton<HuiaSampleSeeder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HuiaSampleSeeder>());

if (builder.Environment.IsDevelopment() || enableE2E)
{
    builder.Services.AddSingleton<CapturingSmsSender>();
    builder.Services.AddScoped<Huia.AspNetCore.Services.ISmsSender>(sp => sp.GetRequiredService<CapturingSmsSender>());

    // With Mailpit (or any SMTP host) configured, keep the real MailKit sender — the E2E suite reads the
    // message back from Mailpit's REST API. Only fall back to the in-memory capturer when nothing is set.
    if (!smtpConfigured)
    {
        builder.Services.AddSingleton<CapturingEmailSender>();
        builder.Services.AddScoped<Huia.AspNetCore.Emails.IHuiaEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
    }
}

var app = builder.Build();

app.UseHuia();

app.MapHuiaEndpoints();

app.MapHuiaAdminEndpoints()
    .RequireAuthorization(policy => policy.RequireTenants("master").RequireRole(HuiaConstants.Roles.Administrator));

app.MapHuiaHome("master");

if (enableE2E)
{
    app.MapGet("/e2e-otp", (string phone, CapturingSmsSender sms) =>
        sms.LastCode(phone) is { } code ? Results.Ok(new { phone, code }) : Results.NotFound());

    if (!smtpConfigured)
    {
        app.MapGet("/e2e-mail", (string email, CapturingEmailSender mail) =>
            mail.LastUrl(email) is { } url ? Results.Ok(new { email, url }) : Results.NotFound());
    }

    app.MapGet("/{tenant}/e2e-callback", (HttpContext ctx) =>
        Results.Text("code=" + ctx.Request.Query["code"] + "&state=" + ctx.Request.Query["state"]));
}

app.Run();

/// <summary>Test entry point marker for <c>WebApplicationFactory</c>.</summary>
public partial class Program;
