using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

const string todoAppUrl = "http://todo-app.dev.localhost:3000";
const string adminAppUrl = "http://admin-app.dev.localhost:3001";
const string shopAppUrl = "http://shop-app.dev.localhost:3002";
const string todoNextUrl = "http://todo-next.dev.localhost:3050";
const string shopNextUrl = "http://shop-next.dev.localhost:3060";
// Shop.Api's own fixed dev port (Properties/launchSettings.json pins the same value) — referenced as a
// plain literal, not shopApi.GetEndpoint(...), because huia-external needs it (ShopConsumer__BaseUrl,
// below) and shop-api itself WaitFor()s huia-external: an endpoint-reference here in both directions
// is a genuine circular dependency that leaves huia-external stuck (never even reaches DCP).
const string shopApiUrl = "https://shop-api.dev.localhost:5341";

// E2E toggle and Postgres-volume toggle are configuration-driven so the Aspire.Hosting.Testing
// fixture can flip them (E2E on, ephemeral database) without editing this file.
var enableE2E = builder.Configuration.GetValue("Huia:EnableE2E", false);
var usePostgresVolume = builder.Configuration.GetValue("Huia:UsePostgresVolume", true);

// Pin the Postgres password (value in appsettings.json / user secrets) so it survives across runs —
// otherwise Aspire generates a fresh random password each run and the persisted data volume, which was
// initialised with the first run's password, rejects it ("password authentication failed for user postgres").
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

var postgresServer = builder.AddPostgres("postgres", password: postgresPassword, port: 59927);
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
var shopPostgres = postgresServer.AddDatabase("shop");

// Shared Redis. The Nuxt sample apps (todo-app, admin-app) mount their server-side session/token
// store on it via Nitro's `redis` storage driver, so sign-in state survives an app restart and is
// shared across instances.
var redis = builder.AddRedis("redis").WithRedisInsight();

if (!enableE2E)
{
    redis = redis.WithDataVolume("huia-redis-data");
}

// Aspire 13.5's AddRedis switches the primary endpoint to TLS whenever a dev HTTPS certificate is
// present. Its own health check — and the plain ioredis clients in the Nuxt apps — can't speak TLS,
// so Redis logs "Error accepting a client connection: wrong version number" on a loop, never turns
// healthy, and everything that WaitFor()s it stays stuck. Opt this resource out: plaintext, one port.
#pragma warning disable ASPIRECERTIFICATES001
redis.WithAnnotation(
    new HttpsCertificateAnnotation { UseDeveloperCertificate = false },
    ResourceAnnotationMutationBehavior.Replace);
#pragma warning restore ASPIRECERTIFICATES001

// redis://:{password}@{host}:{port} — Aspire builds this (URI-encoded password, right scheme/host/port)
// so it stays correct whether or not TLS is in play.
var redisUrl = redis.Resource.UriExpression;

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
    .WithEndpoint("https", endpoint => endpoint.TargetHost = "external.dev.localhost")
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("Huia__Database", "Sqlite")
    .WithEnvironment("ShopConsumer__BaseUrl", shopApiUrl);

// Huia.Headless is single-tenant and entirely self-contained — its bearer tokens are only valid
// against the app that minted them, so unlike todoApi/identityServer it never shares a database with
// another service; it still gets its own dedicated "shop" Postgres database below.
var shopApi = builder.AddProject<Projects.Shop_Api>("shop-api")
    .WithEndpoint("https", endpoint => endpoint.TargetHost = "shop-api.dev.localhost")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Huia__EnableE2E", enableE2E ? "true" : "false")
    .WithEnvironment("Shop__AppUrl", shopAppUrl)
    .WithEnvironment("Shop__NextAppUrl", shopNextUrl)
    .WithEnvironment("Huia__ExternalIssuer", external.GetEndpoint("https"))
    .WithReference(shopPostgres)
    .WaitFor(shopPostgres)
    .WithReference(external)
    .WaitFor(external);

shopApi.WithEnvironment("Huia__Issuer", shopApiUrl);

// Huia.External needs Shop.Api's own base URL (it's the OIDC relying party for the "shop-api" client
// registered there, not Shop.App — see samples/Huia.External/Program.cs). Passed as the shopApiUrl
// literal above (not shopApi.GetEndpoint("https")) to avoid the circular reference explained there.

var identityServer = builder.AddProject<Projects.Huia_IdentityServer>("huia-identityserver")
    .WithEndpoint("https", endpoint => endpoint.TargetHost = "identityserver.dev.localhost")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithReference(mailpit.GetEndpoint("smtp"))
    .WaitFor(mailpit)
    .WithEnvironment("Huia__Database", "Postgres")
    .WithEnvironment("Huia__EnableE2E", enableE2E ? "true" : "false")
    .WithEnvironment("Clients__TodoApp__BaseUrl", todoAppUrl)
    .WithEnvironment("Clients__TodoNext__BaseUrl", todoNextUrl)
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
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "todo-api.dev.localhost")
    .WithExternalHttpEndpoints()

    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Scalar UI";
        url.Url = "/scalar"; // Appends /scalar to the base URL
    })
    .WithReference(identityServer)
    .WaitFor(identityServer)
    .WithReference(todoPostgres)
    .WaitFor(todoPostgres)
    .WithEnvironment("Huia__BaseUrl", identityServer.GetEndpoint("https"))
    .WithEnvironment("Todo__Database", "Postgres");

// The externally reachable origin of the API — used to build the Scalar reference's OAuth redirect
// URI, which must match the redirect URI the identity server registers for the "todo-api-docs" client.
todoApi.WithEnvironment("Todo__PublicUrl", todoApi.GetEndpoint("http"));
identityServer.WithEnvironment("Clients__TodoApi__BaseUrl", todoApi.GetEndpoint("http"));



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
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "todo-app.dev.localhost")
    .WaitFor(identityServer)
    .WaitFor(todoApi)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("NUXT_REDIS_URL", redisUrl)
    .WithEnvironment("NUXT_PUBLIC_HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("NUXT_PUBLIC_TODO_API_URL", todoApi.GetEndpoint("http"))
    .WithEnvironment("NUXT_HUIA_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("NUXT_HUIA_CLIENT_SECRET", "todo-app-secret")
    .WithEnvironment("NUXT_HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("NUXT_HUIA_TENANT", "todo")
    // Node's undici rejects the ASP.NET Core dev cert; this also lets nuxt-huia-oidc discover it.
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithNpm()
    ;

builder.AddViteApp("admin-app", "../Huia.AdminUI")
    // Unproxied on a fixed port so the app's origin matches its registered OIDC redirect URI
    // (http://localhost:3001/...) under both `aspire run` and Aspire.Hosting.Testing.
    .WithHttpEndpoint(port: 3001, targetPort: 3001, env: "PORT", isProxied: false)
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "admin-app.dev.localhost")
    .WaitFor(identityServer)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("NUXT_REDIS_URL", redisUrl)
    .WithEnvironment("NUXT_PUBLIC_HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("NUXT_HUIA_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("NUXT_HUIA_CLIENT_SECRET", "huia-admin-ui-secret")
    .WithEnvironment("NUXT_HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("NUXT_HUIA_TENANT", "master")
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithNpm()
    ;

builder.AddViteApp("shop-app", "../Shop.App")
    .WithHttpEndpoint(port: 3002, env: "PORT")
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "shop-app.dev.localhost")
    .WaitFor(shopApi)
    .WithEnvironment("NUXT_PUBLIC_SHOP_API_URL", shopApi.GetEndpoint("https"))
    .WithEnvironment("NUXT_HUIA_HEADLESS_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    // Node's undici rejects the ASP.NET Core dev cert; this also lets nuxt-huia-headless call Shop.Api.
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithNpm()
    ;

builder.AddNextJsApp("todo-next", "../Todo.Next")
    .WithHttpEndpoint(port: 3050, env: "PORT")
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "todo-next.dev.localhost")
    .WaitFor(identityServer)
    .WaitFor(todoApi)
    .WithEnvironment("PORT", "3050")
    .WithEnvironment("HUIA_BASE_URL", identityServer.GetEndpoint("https"))
    .WithEnvironment("TODO_API_URL", todoApi.GetEndpoint("http"))
    .WithEnvironment("HUIA_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("HUIA_CLIENT_ID", "todo-next")
    .WithEnvironment("HUIA_CLIENT_SECRET", "todo-next-secret")
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    ;

builder.AddNextJsApp("shop-next", "../Shop.Next")
    .WithHttpEndpoint(port: 3060, env: "PORT")
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "shop-next.dev.localhost")
    .WaitFor(shopApi)
    .WithEnvironment("PORT", "3060")
    .WithEnvironment("SHOP_API_URL", shopApi.GetEndpoint("https"))
    .WithEnvironment("HUIA_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
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
