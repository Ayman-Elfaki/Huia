using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

const string todoAppUrl = "http://localhost:3000";
const string adminAppUrl = "http://localhost:3001";

// E2E toggle and Postgres-volume toggle are configuration-driven so the Aspire.Hosting.Testing
// fixture can flip them (E2E on, ephemeral database) without editing this file.
var enableE2E = builder.Configuration.GetValue("Huia:EnableE2E", false);
var usePostgresVolume = builder.Configuration.GetValue("Huia:UsePostgresVolume", true);

// Pin the Postgres password (value in appsettings.json / user secrets) so it survives across runs —
// otherwise Aspire generates a fresh random password each run and the persisted data volume, which was
// initialised with the first run's password, rejects it ("password authentication failed for user postgres").
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

var postgresServer = builder.AddPostgres("postgres", password: postgresPassword,port:59927);
if (usePostgresVolume)
{
    // Huia ships no EF migrations by design (SchemaInitializer just calls EnsureCreatedAsync), so a
    // schema change (e.g. a new column) is never applied to an already-existing database. If
    // huia-identityserver starts crash-looping with a Postgres "column ... does not exist" error after
    // pulling new code, delete the stale volume (`docker volume ls` for a name like
    // "huia.apphost-*-postgres-data", then `docker volume rm`) — the next run recreates it fresh and
    // reseeds everything. Or set Huia:UsePostgresVolume=false for a one-off ephemeral run instead.
    postgresServer.WithDataVolume();
}

var postgres = postgresServer.AddDatabase("huia");
var todoPostgres = postgresServer.AddDatabase("todo");

// Mailpit is the local SMTP sink: it accepts every message on the `smtp` endpoint (no auth, no TLS)
// and exposes a web UI + REST API on the `http` endpoint. The identity server sends real mail here in
// `aspire run` and the E2E suite asserts against the REST API. Pinned to the tag CI pre-pulls.
var mailpit = builder.AddMailPit("mailpit");

if (!enableE2E)
{
    // Keep captured mail across `aspire run` sessions; the E2E suite wants a clean sink each run.
    mailpit = mailpit.WithDataVolume("mailpit-data");
}

// Huia maps /health/live (process) and /health/ready (database reachable + start-up seeding complete).
// Aspire polls /health/ready, so a resource only turns "healthy" — and anything that WaitFor()s it only
// starts — once the identity provider is actually ready to serve.
var external = builder.AddProject<Projects.Huia_External>("huia-external")
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("Huia__Database", "Sqlite");

var identityServer = builder.AddProject<Projects.Huia_IdentityServer>("huia-identityserver")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithReference(mailpit.GetEndpoint("smtp"))
    .WaitFor(mailpit)
    .WithEnvironment("Huia__Database", "Postgres")
    .WithEnvironment("Huia__EnableE2E", enableE2E ? "true" : "false")
    .WithEnvironment("Clients__TodoApp__BaseUrl", todoAppUrl)
    .WithEnvironment("Clients__AdminApp__BaseUrl", adminAppUrl)
    .WithEnvironment("Huia__ExternalIssuer", external.GetEndpoint("https"))
    .WithEnvironment(context =>
    {
        var smtp = mailpit.GetEndpoint("smtp");
        context.EnvironmentVariables["Huia__Email__Host"] = smtp.Property(EndpointProperty.Host);
        context.EnvironmentVariables["Huia__Email__Port"] = smtp.Property(EndpointProperty.Port);
        context.EnvironmentVariables["Huia__Email__UseSsl"] = "false";
        context.EnvironmentVariables["Huia__Email__FromAddress"] = "no-reply@huia.local";
        context.EnvironmentVariables["Huia__Email__FromName"] = "Huia";
    })
    .WithHttpHealthCheck("/health/ready")
    .WithReference(external)
    .WaitFor(external);

var todoApi = builder.AddProject<Projects.Todo_Api>("todo-api")
    .WithReference(identityServer)
    .WaitFor(identityServer)
    .WithReference(todoPostgres)
    .WaitFor(todoPostgres)
    .WithEnvironment("Huia__BaseUrl", identityServer.GetEndpoint("https"))
    .WithEnvironment("Todo__Database", "Postgres");

// The admin CLI (device-authorization grant against the master tenant). It runs one command and exits,
// so it does not start with the rest of the graph — press "Start" in the dashboard to open it in a
// terminal (e.g. `huia login`, which waits for you to approve the device code in the browser).
builder.AddProject<Projects.Huia_Cli>("huia-cli")
    .WithArgs("--issuer", identityServer.GetEndpoint("https"), "--tenant", "master")
    .WithExplicitStart()
    .WithTerminal()
    .WaitFor(identityServer);


builder.AddViteApp("todo-app", "../Todo.App")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WaitFor(identityServer)
    .WaitFor(todoApi)
    .WithEnvironment("NUXT_PUBLIC_HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("NUXT_PUBLIC_TODO_API_URL", todoApi.GetEndpoint("http"))
    .WithEnvironment("NUXT_HUIA_AUTH_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("NUXT_HUIA_AUTH_CLIENT_SECRET", "todo-app-secret")
    .WithEnvironment("NUXT_HUIA_AUTH_HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("NUXT_HUIA_AUTH_HUIA_TENANT", "todo")
    // Node's undici rejects the ASP.NET Core dev cert; this also lets huia-nuxt discover it.
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithNpm()
    ;

builder.AddViteApp("admin-app", "../Huia.AdminUI")
    // Unproxied on a fixed port so the app's origin matches its registered OIDC redirect URI
    // (http://localhost:3001/...) under both `aspire run` and Aspire.Hosting.Testing.
    .WithHttpEndpoint(port: 3001, targetPort: 3001, env: "PORT", isProxied: false)
    .WaitFor(identityServer)
    .WithEnvironment("NUXT_PUBLIC_HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("NUXT_HUIA_AUTH_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("NUXT_HUIA_AUTH_CLIENT_SECRET", "huia-admin-ui-secret")
    .WithEnvironment("NUXT_HUIA_AUTH_HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("NUXT_HUIA_AUTH_HUIA_TENANT", "master")
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithNpm()
    ;

builder.Build().Run();

static string GenerateRandomUrlSafeString(int length = 48)
{
    var randomBytes = new byte[length];
    RandomNumberGenerator.Fill(randomBytes);

    return Convert.ToBase64String(randomBytes)
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');
}
