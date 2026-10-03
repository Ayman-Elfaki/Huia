using Huia;
using Huia.OpenId.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Todo.IdentityServer;
using Todo.IdentityServer.Tenants;
using Huia.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databaseProvider = builder.Configuration.GetValue("Huia:Database", "Sqlite");
var enableE2E = builder.Configuration.GetValue("Huia:EnableE2E", false);

var issuer = builder.Configuration.GetValue("Huia:Issuer", "https://localhost:5310");
var todoApiUrl = builder.Configuration.GetValue("Clients:TodoApi:BaseUrl", "http://localhost:5330");

var todoAppUrl = builder.Configuration.GetValue("Clients:TodoApp:BaseUrl", "https://localhost:3000");
var todoNextUrl = builder.Configuration.GetValue("Clients:TodoNext:BaseUrl", "https://localhost:3050");
var adminAppUrl = builder.Configuration.GetValue("Clients:AdminApp:BaseUrl", "https://localhost:3001");


// The huia-nuxt module's own E2E playground (EnableE2E only).
var playgroundUrl = builder.Configuration.GetValue("Clients:PlaygroundApp:BaseUrl", "http://localhost:3030");
// Huia.External is the mock upstream IdP the "todo" tenant's external-login button federates to.
var externalIssuer = builder.Configuration.GetValue("Huia:ExternalIssuer", "https://localhost:5320");

// A single shared in-memory SQLite connection kept open for the process lifetime.
SqliteConnection? sqliteConnection;
if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("huia") ?? "DataSource=:memory:";
    sqliteConnection = new SqliteConnection(connectionString);
    sqliteConnection.Open();
    builder.Services.AddSingleton(sqliteConnection);
    builder.Services.AddDbContext<IdentityHuiaDbContext>(options =>
        options.UseSqlite(sqliteConnection).UseOpenIddict());
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("huia")
                           ?? throw new InvalidOperationException(
                               "A 'huia' connection string is required for the Postgres provider.");
    builder.Services.AddDbContext<IdentityHuiaDbContext>(options =>
        options.UseNpgsql(connectionString).UseOpenIddict());
}

// The schema initializer runs before any Huia hosted service (registration order).
builder.Services.AddHostedService<SchemaInitializer>();

// True once an SMTP host is configured (via Aspire's Mailpit wiring, or appsettings). When set, the real
// MailKit sender is used and the in-memory CapturingEmailSender / /e2e-mail fallback is left out.
var smtpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Huia:Email:Host"]);

var huiaBuilder = builder.Services.AddHuiaOpenId(huia =>
{
    huia.UseIssuer(issuer);
    huia.UsePublicUrl(issuer);
    if (builder.Environment.IsDevelopment() || enableE2E)
    {
        huia.DisableTransportSecurityRequirement();
    }

    huia.ConfigureEmail(email => builder.Configuration.GetSection("Huia:Email").Bind(email));
    huia.ConfigureSms(sms => sms.LogCodesToLogger = builder.Environment.IsDevelopment());
    huia.ConfigureKeys(keys =>
        keys.EnableBackgroundJobs = builder.Configuration.GetValue("Huia:EnableBackgroundJobs", true));
    huia.ConfigureCleanup(cleanup =>
        cleanup.EnableBackgroundJobs = builder.Configuration.GetValue("Huia:EnableBackgroundJobs", true));

    var googleClientId = builder.Configuration["Google:ClientId"];
    var googleClientSecret = builder.Configuration["Google:ClientSecret"];

    huia.AddTenant(new MasterTenant(issuer, adminAppUrl));
    huia.AddTenant(new TodoTenant(issuer, todoAppUrl, todoNextUrl, todoApiUrl, externalIssuer, googleClientId,
        googleClientSecret));

    if (enableE2E)
    {
        huia.AddTenant(new E2ETenant(issuer, playgroundUrl));
        huia.AddTenant(new E2ESignupTenant());
    }
});

huiaBuilder
    .ConfigureOpenIddictServer(server =>
    {
        // Lower-level OpenIddict server hooks exposed directly to host applications
    })
    .ConfigureFinbuckle(finbuckle =>
    {
        // Lower-level Finbuckle multitenancy hooks exposed directly to host applications
    })
    .AddEntityFrameworkCoreStores<IdentityHuiaDbContext, HuiaUser, HuiaRole>()
    .AddHuiaUi()
    .AddHuiaSecurityHeaders();

builder.Services.AddSingleton<HuiaSampleSeeder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HuiaSampleSeeder>());

if (builder.Environment.IsDevelopment() || enableE2E)
{
    builder.Services.AddSingleton<CapturingSmsSender>();
    builder.Services.AddScoped<Huia.Services.ISmsSender>(sp => sp.GetRequiredService<CapturingSmsSender>());

    // With Mailpit (or any SMTP host) configured, keep the real MailKit sender — the E2E suite reads the
    // message back from Mailpit's REST API. Only fall back to the in-memory capturer when nothing is set.
    if (!smtpConfigured)
    {
        builder.Services.AddSingleton<CapturingEmailSender>();
        builder.Services.AddScoped<Huia.OpenId.Emails.IHuiaEmailSender>(sp =>
            sp.GetRequiredService<CapturingEmailSender>());
    }
}

var app = builder.Build();

app.UseHuiaOpenId();

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
