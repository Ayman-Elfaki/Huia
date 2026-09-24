using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Huia.AppHost;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

const string adminAppUrl = "http://admin-app.dev.localhost:3001";
const string todoAppUrl = "http://todo-app.dev.localhost:3000";
const string todoNextUrl = "http://todo-next.dev.localhost:3050";
const string shopAppUrl = "http://shop-app.dev.localhost:3002";
const string shopNextUrl = "http://shop-next.dev.localhost:3060";
// huia-external, shop-api, huia-identityserver and todo-api below all run unproxied on these fixed dev
// ports (Properties/launchSettings.json pins the same values). Unlike the browser-facing app URLs above,
// their own URLs stay on plain "localhost" rather than a *.dev.localhost alias: every one of them is also
// reached by a *server-side* HTTP call from another resource — OIDC/JWKS discovery fetches from .NET
// (huia-external from shop-api/huia-identityserver, huia-identityserver from todo-api) and from Node
// (shop-api from Shop.Nuxt/Shop.Next's server routes) — and neither .NET's HttpClient nor Node's fetch get
// a browser's special-cased "*.localhost is loopback" resolution, so a *.dev.localhost value here fails
// outright ("No such host is known") the moment anything but a browser tries to reach it. Tried making
// huia-external's own identity a *.dev.localhost exception (its client redirect URIs still register
// under it, since that half is just strings, never fetched) — but huia-external derives its issuer, and
// every URL in its discovery document, from whichever hostname *the discovery request itself* arrived on
// (its own multi-tenant-by-domain design), so shop-api/huia-identityserver's server-side discovery fetch
// would have to genuinely go out as that hostname too, not just resolve *to* it — and OpenIddict's own
// HTTP client explicitly rejects any primary handler that isn't a plain HttpClientHandler, which rules out
// the usual SocketsHttpHandler.ConnectCallback trick for that. "localhost" resolves everywhere (browser
// included), so it's used consistently for each one's own self-referential Issuer/PublicUrl and
// everywhere else that needs to reach it directly. They're literals, not <resource>.GetEndpoint(...),
// partly for that reason and partly to sidestep real circular references: e.g. huia-external needs
// shop-api's URL (ShopConsumer__BaseUrl) while shop-api itself WaitFor()s huia-external, so an
// endpoint-reference in both directions would leave huia-external stuck (never even reaching DCP).
const string shopApiUrl = "https://localhost:5341";
const string identityServerUrl = "https://localhost:5310";
const string externalUrl = "https://localhost:5320";
const string todoApiUrl = "http://localhost:5330";

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
    // Unproxied on its fixed dev port (Properties/launchSettings.json pins 5320) so it's reachable at
    // the same origin under both `aspire run` and Aspire.Hosting.Testing — which otherwise reassigns
    // proxied endpoints a random port per run. TargetHost is purely a manually-browsable alias for
    // `aspire run`'s dashboard (see the externalUrl comment above for why config stays on "localhost").
    .WithEndpoint("https", endpoint =>
    {
        endpoint.TargetHost = "external.dev.localhost";
        endpoint.Port = 5320;
        endpoint.TargetPort = 5320;
        endpoint.IsProxied = false;
    })
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("Huia__Database", "Sqlite")
    .WithEnvironment("Huia__Issuer", externalUrl)
    .WithEnvironment("Consumer__BaseUrl", identityServerUrl)
    .WithEnvironment("ShopConsumer__BaseUrl", shopApiUrl)
    .WithBuildE2EArtifactCommand("samples/Shared/Huia.External/Huia.External.csproj");

// Huia.Headless is single-tenant and entirely self-contained — its bearer tokens are only valid
// against the app that minted them, so unlike todoApi/identityServer it never shares a database with
// another service; it still gets its own dedicated "shop" Postgres database below.
var shopApi = builder.AddProject<Projects.Shop_Api>("shop-api")
    // Unproxied on its fixed dev port (Properties/launchSettings.json pins 5341) — same reasons as
    // huia-external above (including TargetHost being dashboard-only).
    .WithEndpoint("https", endpoint =>
    {
        endpoint.TargetHost = "shop-api.dev.localhost";
        endpoint.Port = 5341;
        endpoint.TargetPort = 5341;
        endpoint.IsProxied = false;
    })
    .WithExternalHttpEndpoints()
    .WithEnvironment("Huia__EnableE2E", enableE2E ? "true" : "false")
    .WithEnvironment("Shop__AppUrl", shopAppUrl)
    .WithEnvironment("Shop__NextAppUrl", shopNextUrl)
    .WithEnvironment("Huia__ExternalIssuer", externalUrl)
    .WithReference(shopPostgres)
    .WaitFor(shopPostgres)
    .WithReference(external)
    .WaitFor(external)
    .WithBuildE2EArtifactCommand("samples/Shop/Shop.Api/Shop.Api.csproj");

shopApi.WithEnvironment("Huia__Issuer", shopApiUrl);

// Huia.External needs Shop.Api's own base URL (it's the OIDC relying party for the "shop-api" client
// registered there, not Shop.Nuxt — see samples/Shared/Huia.External/Program.cs).

var identityServer = builder.AddProject<Projects.Todo_IdentityServer>("huia-identityserver")
    // Unproxied on its fixed dev port (Properties/launchSettings.json pins 5310) — same reasons as
    // huia-external above (including TargetHost being dashboard-only).
    .WithEndpoint("https", endpoint =>
    {
        endpoint.TargetHost = "identityserver.dev.localhost";
        endpoint.Port = 5310;
        endpoint.TargetPort = 5310;
        endpoint.IsProxied = false;
    })
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithReference(mailpit.GetEndpoint("smtp"))
    .WaitFor(mailpit)
    .WithEnvironment("Huia__Database", "Postgres")
    .WithEnvironment("Huia__EnableE2E", enableE2E ? "true" : "false")
    .WithEnvironment("Huia__Issuer", identityServerUrl)
    .WithEnvironment("Clients__TodoApp__BaseUrl", todoAppUrl)
    .WithEnvironment("Clients__TodoNext__BaseUrl", todoNextUrl)
    .WithEnvironment("Clients__AdminApp__BaseUrl", adminAppUrl)
    .WithEnvironment("Huia__ExternalIssuer", externalUrl)
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
    .WaitFor(external)
    .WithBuildE2EArtifactCommand("samples/Todo/Todo.IdentityServer/Todo.IdentityServer.csproj")
    .WithBuildAllE2EArtifactsCommand();

var todoApi = builder.AddProject<Projects.Todo_Api>("todo-api")
    // Unproxied on its fixed dev port (Properties/launchSettings.json pins 5330) — same reasons as
    // huia-external above (including TargetHost being dashboard-only).
    .WithEndpoint("http", endpoint =>
    {
        endpoint.TargetHost = "todo-api.dev.localhost";
        endpoint.Port = 5330;
        endpoint.TargetPort = 5330;
        endpoint.IsProxied = false;
    })
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
    .WithEnvironment("Huia__BaseUrl", identityServerUrl)
    .WithEnvironment("Todo__Database", "Postgres")
    .WithBuildE2EArtifactCommand("samples/Todo/Todo.Api/Todo.Api.csproj");

// The externally reachable origin of the API — used to build the Scalar reference's OAuth redirect
// URI, which must match the redirect URI the identity server registers for the "todo-api-docs" client.
todoApi.WithEnvironment("Todo__PublicUrl", todoApiUrl);
identityServer.WithEnvironment("Clients__TodoApi__BaseUrl", todoApiUrl);



// The admin CLI (device-authorization grant against the master tenant). It runs one command and exits,
// so it does not start with the rest of the graph — press "Start" in the dashboard to open it in a
// terminal (e.g. `huia login`, which waits for you to approve the device code in the browser).
builder.AddProject<Projects.Huia_Cli>("huia-cli")
    .WithArgs("--issuer", identityServerUrl, "--tenant", "master")
    .WithExplicitStart()
    .WithTerminal()
    .WaitFor(identityServer);


builder.AddViteApp("todo-app", "../../Todo/Todo.Nuxt")
    // Unproxied on a fixed port, with a TargetHost alias below, so the app's origin matches its
    // registered OIDC redirect URI (http://todo-app.dev.localhost:3000/...) under both `aspire run`
    // and Aspire.Hosting.Testing.
    .WithHttpEndpoint(port: 3000, targetPort: 3000, env: "PORT", isProxied: false)
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "todo-app.dev.localhost")
    .WaitFor(identityServer)
    .WaitFor(todoApi)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("NUXT_REDIS_URL", redisUrl)
    .WithEnvironment("NUXT_PUBLIC_HUIA_BASE_URL", identityServerUrl)
    .WithEnvironment("NUXT_PUBLIC_TODO_API_URL", todoApiUrl)
    .WithEnvironment("NUXT_HUIA_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("NUXT_HUIA_CLIENT_SECRET", "todo-app-secret")
    .WithEnvironment("NUXT_HUIA_BASE_URL", identityServerUrl)
    .WithEnvironment("NUXT_HUIA_TENANT", "todo")
    // Node's undici rejects the ASP.NET Core dev cert; this also lets nuxt-huia-oidc discover it.
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithNpm()
    .WithBuildE2EArtifactCommand("samples/Todo/Todo.Nuxt", isNpm: true)
    ;

builder.AddViteApp("admin-app", "../../Todo/Todo.Admin")
    // Unproxied on a fixed port, with a TargetHost alias below, so the app's origin matches its
    // registered OIDC redirect URI (http://admin-app.dev.localhost:3001/...) under both `aspire run`
    // and Aspire.Hosting.Testing.
    .WithHttpEndpoint(port: 3001, targetPort: 3001, env: "PORT", isProxied: false)
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "admin-app.dev.localhost")
    .WaitFor(identityServer)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("NUXT_REDIS_URL", redisUrl)
    .WithEnvironment("NUXT_PUBLIC_HUIA_BASE_URL", identityServerUrl)
    .WithEnvironment("NUXT_HUIA_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("NUXT_HUIA_CLIENT_SECRET", "todo-admin-secret")
    .WithEnvironment("NUXT_HUIA_BASE_URL", identityServerUrl)
    .WithEnvironment("NUXT_HUIA_TENANT", "master")
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithNpm()
    .WithBuildE2EArtifactCommand("samples/Todo/Todo.Admin", isNpm: true)
    ;

builder.AddViteApp("shop-app", "../../Shop/Shop.Nuxt")
    // Unproxied on a fixed port, with a TargetHost alias below, so the app's origin matches its
    // registered OIDC redirect URI (http://shop-app.dev.localhost:3002/...) under both `aspire run`
    // and Aspire.Hosting.Testing.
    .WithHttpEndpoint(port: 3002, targetPort: 3002, env: "PORT", isProxied: false)
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "shop-app.dev.localhost")
    .WaitFor(shopApi)
    .WithEnvironment("NUXT_PUBLIC_SHOP_API_URL", shopApiUrl)
    .WithEnvironment("NUXT_SHOP_API_URL", shopApiUrl)
    .WithEnvironment("NUXT_HUIA_HEADLESS_BASE_URL", shopApiUrl)
    .WithEnvironment("NUXT_HUIA_HEADLESS_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    // Node's undici rejects the ASP.NET Core dev cert; this also lets nuxt-huia-headless call Shop.Api.
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithNpm()
    .WithBuildE2EArtifactCommand("samples/Shop/Shop.Nuxt", isNpm: true)
    ;

builder.AddNextJsApp("todo-next", "../../Todo/Todo.Next")
    // Unproxied on a fixed port, with a TargetHost alias below, so the app's origin matches its
    // registered OIDC redirect URI (http://todo-next.dev.localhost:3050/...) under both `aspire run`
    // and Aspire.Hosting.Testing. NEXT_PUBLIC_APP_URL feeds next-huia-oidc's appUrl (see auth.config.ts)
    // for the same reason — Next.js's own request.url doesn't reflect this origin either.
    .WithHttpEndpoint(port: 3050, targetPort: 3050, env: "PORT", isProxied: false)
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "todo-next.dev.localhost")
    .WaitFor(identityServer)
    .WaitFor(todoApi)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("PORT", "3050")
    .WithEnvironment("NEXT_PUBLIC_APP_URL", todoNextUrl)
    .WithEnvironment("HUIA_BASE_URL", identityServerUrl)
    .WithEnvironment("TODO_API_URL", todoApiUrl)
    // See the storage doc comment in auth.config.ts — required for token records to survive across
    // Next.js's independently-bundled Route Handlers.
    .WithEnvironment("REDIS_URL", redisUrl)
    .WithEnvironment("HUIA_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("HUIA_CLIENT_ID", "todo-next")
    .WithEnvironment("HUIA_CLIENT_SECRET", "todo-next-secret")
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithBuildE2EArtifactCommand("samples/Todo/Todo.Next", isNpm: true)
    ;

builder.AddNextJsApp("shop-next", "../../Shop/Shop.Next")
    // Unproxied on a fixed port, with a TargetHost alias below, so the app's origin matches its
    // registered OIDC redirect URI (http://shop-next.dev.localhost:3060/...) under both `aspire run`
    // and Aspire.Hosting.Testing. NEXT_PUBLIC_APP_URL feeds next-huia-headless's appUrl (see
    // auth.config.ts) for the same reason — Next.js's own request.url doesn't reflect this origin either.
    .WithHttpEndpoint(port: 3060, targetPort: 3060, env: "PORT", isProxied: false)
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "shop-next.dev.localhost")
    .WaitFor(shopApi)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("PORT", "3060")
    .WithEnvironment("NEXT_PUBLIC_APP_URL", shopNextUrl)
    .WithEnvironment("SHOP_API_URL", shopApiUrl)
    // See the storage doc comment in auth.config.ts — required for token records to survive across
    // Next.js's independently-bundled Route Handlers.
    .WithEnvironment("REDIS_URL", redisUrl)
    .WithEnvironment("HUIA_SESSION_PASSWORD", GenerateRandomUrlSafeString())
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithExternalHttpEndpoints()
    .WithBuildE2EArtifactCommand("samples/Shop/Shop.Next", isNpm: true)
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
