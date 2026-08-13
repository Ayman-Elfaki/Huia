// AppHost.cs is .NET Aspire's conventional file name for the host project's top-level statements (which the
// compiler wraps in an implicit "Program" class) - not a name mismatch to fix.
#pragma warning disable MA0048
var builder = DistributedApplication.CreateBuilder(args);

// Secrets get dev-friendly defaults here so `aspire run` works out of the box; override them via user
// secrets/parameters for anything beyond local development.
var webClientSecret = builder.AddParameter("web-client-secret", "todo-web-dev-secret", secret: true);
var testClientSecret = builder.AddParameter("test-client-secret", "todo-tests-dev-secret", secret: true);
var adminClientSecret = builder.AddParameter("admin-client-secret", "admin-ui-dev-secret", secret: true);
var authSecret = builder.AddParameter("auth-secret", "insecure-dev-only-auth-secret-change-me", secret: true);
var postgresPassword = builder.AddParameter("postgres-password", "todo-postgres-dev-secret", secret: true);

var mailpit = builder.AddMailPit("mailpit");

// One server, one logical database per app. Persistent so local dev data (accounts, todos) survives an
// AppHost restart.
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithLifetime(ContainerLifetime.Persistent);

var todoApiDb = postgres.AddDatabase("todoapidb");

var api = builder.AddProject<Projects.Huia_TodoApi>("todoapi")
    // Pinned and unproxied: TodoApi reports its own issuer (in tokens and the discovery document) based on
    // the address it's actually bound to. Without a fixed target port, the advertised endpoint used by
    // AUTH_HUIA_ISSUER below (api.GetEndpoint("https")) can diverge from that self-reported address, which
    // fails Auth.js's strict issuer check with a generic "Configuration" error.
    .WithHttpsEndpoint(port: 5041, targetPort: 5041, isProxied: false)
    .WithReference(mailpit)
    .WaitFor(mailpit)
    .WithReference(todoApiDb)
    .WaitFor(todoApiDb)
    .WithEnvironment("Oidc__WebClientSecret", webClientSecret)
    .WithEnvironment("Oidc__TestClientSecret", testClientSecret)
    .WithEnvironment("Oidc__AdminClientSecret", adminClientSecret)
    .WithExternalHttpEndpoints();

var web = builder.AddNextJsApp("web", "../Huia.TodoApp")
    // Pinned and unproxied: DCP's dynamic port proxying for this resource was observed forwarding requests
    // with a different Host than the port it advertises to other resources, which broke Auth.js's
    // trustHost-based URL inference (NEXTAUTH sees the wrong origin). Pinning port == targetPort with no
    // proxy removes the indirection entirely.
    .WithHttpEndpoint(port: 3000, targetPort: 3000, isProxied: false)
    .WithReference(api)
    .WaitFor(api)
    // Node has its own CA bundle, separate from the OS/.NET trust store, so it doesn't trust TodoApi's
    // ASP.NET Core HTTPS development certificate. Without this, Auth.js's server-to-server calls to TodoApi
    // (discovery, code-for-token exchange) fail with "fetch failed", surfaced to the browser as a generic
    // "Server error" right after the user signs in. NODE_EXTRA_CA_CERTS can't fix this: the dev cert is a
    // leaf certificate (Basic Constraints CA:FALSE), which Node's strict trust-anchor validation rejects
    // even though browsers accept it fine — so this disables outbound TLS verification for the whole
    // process instead. Safe here because every call this process makes over HTTPS stays on loopback
    // (TodoApi, on the same machine); never do this for a process that talks to the public internet.
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    // Without this, Auth.js falls back to inferring its own base URL from the request on every single
    // request and logs a warning each time it does — see https://next-auth.js.org/warnings#nextauth_url.
    // Literal, not web.GetEndpoint("http"), for the same reason AUTH_HUIA_POST_LOGOUT_REDIRECT_URI below is:
    // this resource can't reference its own endpoint from within its own builder chain, but the port is
    // pinned (see WithHttpEndpoint above), so the literal is exact.
    .WithEnvironment("NEXTAUTH_URL", "http://localhost:3000")
    .WithEnvironment("AUTH_SECRET", authSecret)
    .WithEnvironment("AUTH_HUIA_CLIENT_ID", "todo-web")
    .WithEnvironment("AUTH_HUIA_CLIENT_SECRET", webClientSecret)
    // OpenIddict's discovery document reports the issuer via a bare .NET Uri, which normalizes to a
    // trailing slash (e.g. "https://localhost:5041/"); Auth.js validates the issuer it's given against
    // that value with strict string equality, so this needs the same trailing slash to match.
    .WithEnvironment("AUTH_HUIA_ISSUER", ReferenceExpression.Create($"{api.GetEndpoint("https")}/"))
    // Where Huia sends the browser back to after RP-initiated logout (/connect/logout) clears its own
    // sign-in cookie — matches the same URL registered as the client's post-logout redirect URI below.
    // Literal, not web.GetEndpoint("http"): this resource can't reference its own endpoint from within its
    // own builder chain, but the port is pinned (see WithHttpEndpoint above), so the literal is exact.
    .WithEnvironment("AUTH_HUIA_POST_LOGOUT_REDIRECT_URI", "http://localhost:3000")
    .WithEnvironment("TODO_API_URL", api.GetEndpoint("https"))
    .WithExternalHttpEndpoints();

var adminUi = builder.AddJavaScriptApp("admin-ui", "../Huia.AdminUI", "dev")
    // Pinned and unproxied, same reasoning as "web" above: nuxt-oidc-auth builds its redirect_uri from the
    // request it actually receives, so a proxy that changes the advertised Host would break the exact-match
    // check /connect/token does against the registered redirect URI below.
    .WithHttpEndpoint(port: 3100, targetPort: 3100, isProxied: false)
    .WithReference(api)
    .WaitFor(api)
    // Unlike AddNextJsApp, AddJavaScriptApp doesn't inject a PORT env var matching the endpoint above — left
    // unset, Nitro's dev server just falls back to its own default (3000), colliding with "web". Nitro reads
    // PORT itself, so this is enough; no --port flag on the "dev" script needed.
    .WithEnvironment("PORT", "3100")
    // See "web"'s own copy of this for why: same self-signed dev cert, same Node trust-store gap, same
    // loopback-only safety argument.
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WithEnvironment("HUIA_CLIENT_ID", "admin-ui")
    .WithEnvironment("HUIA_CLIENT_SECRET", adminClientSecret)
    // No trailing slash, unlike AUTH_HUIA_ISSUER on "web" — nuxt.config.ts builds every OIDC endpoint URL by
    // plain string concatenation (`${issuer}/connect/authorize`, etc.), same reasoning as Oidc__Issuer below.
    .WithEnvironment("HUIA_ISSUER", api.GetEndpoint("https"))
    // Literal, not adminUi.GetEndpoint("http"): this resource can't reference its own endpoint from within
    // its own builder chain, but the port is pinned (see WithHttpEndpoint above), so the literal is exact.
    // "/auth/oidc/callback" (not "/auth/admin-ui/callback"): nuxt-oidc-auth's provider key for a generic,
    // non-preset OIDC provider is fixed to "oidc" (see nuxt.config.ts) — that's what it derives every
    // login/callback/logout route from, regardless of what we name the client on Huia's side.
    .WithEnvironment("HUIA_REDIRECT_URI", "http://localhost:3100/auth/oidc/callback")
    // Same literal-endpoint reasoning as HUIA_REDIRECT_URI above. Matches Oidc__AdminPostLogoutRedirectUri
    // below, which is what api's admin-ui client registration actually validates against — without this,
    // nuxt-oidc-auth's logout handler never sends a post_logout_redirect_uri at all (see nuxt.config.ts),
    // so /connect/logout has nowhere registered to send the browser back to.
    .WithEnvironment("HUIA_POST_LOGOUT_REDIRECT_URI", "http://localhost:3100")
    .WithExternalHttpEndpoints();

// The web/admin-ui apps' own callback/post-logout URLs aren't known until they're declared, so the api's
// client registrations pick them up here rather than at the AddProject call above.
api.WithEnvironment("Oidc__WebRedirectUri",
        ReferenceExpression.Create($"{web.GetEndpoint("http")}/api/auth/callback/huia"))
    .WithEnvironment("Oidc__WebPostLogoutRedirectUri", web.GetEndpoint("http"))
    .WithEnvironment("Oidc__AdminRedirectUri",
        ReferenceExpression.Create($"{adminUi.GetEndpoint("http")}/auth/oidc/callback"))
    .WithEnvironment("Oidc__AdminPostLogoutRedirectUri", adminUi.GetEndpoint("http"))
    .WithEnvironment("Oidc__AdminHomeUri", adminUi.GetEndpoint("http"))
    // TodoApi's own ResolveIssuer (Program.cs) falls back to parsing ASPNETCORE_URLS when this isn't set;
    // pinning it explicitly here keeps the issuer deterministic regardless of what Kestrel actually binds
    // to. No trailing slash, unlike AUTH_HUIA_ISSUER above: this value also feeds
    // Program.cs's own `$"{issuer}/scalar"`-style redirect URIs and Scalar's OAuth flow config via plain
    // string concatenation (not a normalizing `new Uri(...)`, unlike SetIssuer itself), so a trailing slash
    // here would double up into "https://localhost:5041//scalar" — a 404, since ASP.NET Core's router
    // doesn't collapse repeated slashes.
    .WithEnvironment("Oidc__Issuer", api.GetEndpoint("https"));


builder.Build().Run();