using Huia.Core;
using Huia.Endpoints;
using Huia.EntityFrameworkCore.Extensions;
using Huia.Eventing;
using Huia.Identity;
using Huia.TodoApi.Common;
using Huia.TodoApi.Data;
using Huia.TodoApi.Email;
using Huia.TodoApi.Endpoints;
using Huia.TodoApi.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Huia's own tables and the sample's Todos table live in the same physical database (see HuiaAppDbContext),
// separated by schema ("huia" and "todos" respectively) — PostgreSQL's native schema support means each
// context's own "__EFMigrationsHistory" table lives inside its own schema automatically, with no risk of the
// two colliding.
var appConnectionString = builder.Configuration.GetConnectionString("todoapidb")!;

builder.Services.AddDbContext<TodoDbContext>(options => options.UseNpgsql(appConnectionString));

builder.Services.AddDbContext<HuiaAppDbContext>(options => options.UseNpgsql(appConnectionString));

// Registered before AddHuia so it wins over the NoOpEmailSender that AddHuia registers with TryAddSingleton.
builder.Services.AddSingleton<IEmailSender<HuiaUser>, SmtpEmailSender>();

// Gives every newly-registered account a TodoUser row (see TodoUserRegisteredHandler) — the sample's
// demonstration of Huia's eventing hook.
builder.Services.AddScoped<IEventHandler<UserRegisteredEvent>, TodoUserRegisteredHandler>();

var issuer = builder.ResolveIssuer();

builder.Services.AddHuia(issuer, huia =>
    {
        huia.Branding.Title = "Todo";

        huia.Branding.ShowTopbar = false;
        // Served from this app's own wwwroot (see app.UseStaticFiles() below) rather than reusing Huia's
        // favicon, so the sample demonstrates LogoUrl with a brand asset distinct from Huia's own.
        huia.Branding.LogoUrl = "/huia-icon.svg";

        // The Next.js app: a server-rendered client (Auth.js runs on the Next.js server, never in the
        // browser), so it holds a secret rather than using the public SPA/PKCE client type.
        huia.Applications.AddServerSideWebApplication(app =>
        {
            app.SetClientId("todo-web");
            app.SetClientSecret(builder.Configuration["Oidc:WebClientSecret"]!);
            app.SetDisplayName("Todo Web App");

            app.AddRedirectUri(builder.Configuration["Oidc:WebRedirectUri"]!);
            app.AddPostLogoutRedirectUri(builder.Configuration["Oidc:WebPostLogoutRedirectUri"]!);

            // Where Huia sends the browser back to this client when there's no live OAuth request to resume
            // (an email confirmation/reset link clicked out of band, or the error page's "return home" link)
            // — its own "Sign in" button starts a fresh, correctly-signed flow from there. Only the client
            // itself can do that (Auth.js callback expects a state/PKCE pair it generated), so this is as
            // far as Huia can automatically take the user.
            app.SetHomeUri(builder.Configuration["Oidc:WebHomeUri"]!);

            // Short-lived access tokens for the sample's own demonstration of token refresh (see RefreshTokenEndpoints).
            app.SetAccessTokenLifetime(TimeSpan.FromMinutes(5));

            // profile/email so Auth.js default OIDC scope request (which includes them) is honored; todos
            // so the issued access token can call the CRUD endpoints below. roles/reports back the
            // admin-only reporting demo below — every signed-in user's token carries them (this client is
            // *allowed* to ask for cross-user reports), but ReportsEndpoints additionally requires the
            // "Admin" role, so only an admin's token actually gets past its authorization policy.
            app.AllowScopes("profile", "email", "todos", "roles", "reports");
        });

        // Huia.AdminUI: a Nuxt app using nuxt-oidc-auth, whose server holds the client secret (same
        // confidential server-side pattern as todo-web above, just a different framework on the other end).
        huia.Applications.AddServerSideWebApplication(app =>
        {
            app.SetClientId("admin-ui");
            app.SetClientSecret(builder.Configuration["Oidc:AdminClientSecret"]!);
            app.SetDisplayName("Huia Admin UI");

            app.AddRedirectUri(builder.Configuration["Oidc:AdminRedirectUri"]!);
            app.AddPostLogoutRedirectUri(builder.Configuration["Oidc:AdminPostLogoutRedirectUri"]!);
            app.SetHomeUri(builder.Configuration["Oidc:AdminHomeUri"]!);

            // "todos" carries no meaning to AdminUI itself (it never calls /api/todos) but is required
            // anyway: TodoApi's RequireAudiences("todo-api") below validates every token server-wide,
            // before any endpoint-specific authorization runs, and only a token that requested "todos" ends
            // up with "todo-api" in its aud claim (see AdminTestHelpers in the integration tests for the
            // same requirement proven against a real token). "roles" is what actually matters here — it's
            // how the admin console tells whether the signed-in user has the "Admin" role
            // MapHuiaAdminEndpoints is gated behind.
            app.AllowScopes("profile", "email", "todos", "roles");
        });

        // Scalar's "Authorize" button runs the authorization code + PKCE flow entirely in the browser, so
        // it's a public SPA client like any other — never holds a secret. The redirect lands back on
        // Scalar's own reference page, which reads the code from the URL and finishes the exchange itself.
        huia.Applications.AddSinglePageApplication(app =>
        {
            app.SetClientId("scalar");
            app.SetDisplayName("Scalar API Reference");
            app.AddRedirectUri($"{issuer}/scalar");
            app.AddRedirectUri($"{issuer}/scalar/v1");
            // Without this, ConfirmEmail's "Sign in" link (clicked out of band, with no live OAuth request to
            // resume — see ClientHomeResolver) falls back to this client's redirect_uri origin alone:
            // "https://localhost:5041" with no path, which TodoApi has no route mapped at and 404s. todo-web
            // doesn't need this itself only because its redirect_uri's origin already IS its home page.
            app.SetHomeUri($"{issuer}/scalar");
            app.AllowScopes("todos");
        });

        // A machine-to-machine client so integration/e2e tests can obtain a token without driving a browser
        // through the interactive login page.
        huia.Applications.AddMachine2Machine(app =>
        {
            app.SetClientId("todo-tests");
            app.SetClientSecret(builder.Configuration["Oidc:TestClientSecret"]!);
            app.AllowScopes("todos", "reports");
        });

        // A device client so integration/e2e tests
        huia.Applications.AddDevice(app =>
        {
            app.SetClientId("huia-cli");
            app.SetDisplayName("Huia CLI");
            app.AllowScopes("roles", "todos");
        });

        // Demonstrates Huia's external-provider support (docs/external-providers.md): once a Google client
        // is registered, "Sign in with Google" appears on the login page automatically — no other wiring
        // needed. Skipped when no client id is configured (e.g. a fresh clone before the sample's own
        // appsettings.json/user-secrets carry one) so the sample still starts without it.
        var googleClientId = builder.Configuration["Google:ClientId"];
        if (!string.IsNullOrEmpty(googleClientId))
        {
            huia.ExternalLogins.AddGoogle(google =>
            {
                google.ClientId = googleClientId;
                google.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
            });
        }

        // Also demonstrates the opt-in password-confirmed linking shortcut: an external sign-in whose email
        // matches an existing password account links itself once the user proves ownership by entering that
        // password, instead of always requiring a separate sign-in-then-link-from-settings round trip.
        huia.EnableExternalLoginPasswordLinking();

        huia.KeysManagement.UseAutomaticKeyManagement();
        
        // A token's aud claim is only populated for scopes that have a resource registered against them —
        // this seeds "todos" with one on startup, idempotently, the same way huia.Applications above seeds
        // client applications.
        huia.Scopes.Add("todos", scope => scope.SetResource("todo-api"));

        // A second, distinct resource from "todo-api" — see ReportsEndpoints, which requires this specific
        // audience (not just any authenticated token) on top of the "reports" scope and "Admin" role. A
        // token only carries "reports-api" in its aud claim when "reports" was actually among the scopes
        // granted at authorization time, same mechanism as "todos"/"todo-api" above.
        huia.Scopes.Add("reports", scope => scope.SetResource("reports-api"));

        // Requires every access token reaching this API to have been minted for it specifically — see the
        // "todos" scope seeding above, which is what actually puts "todo-api" on a token's aud claim.
        huia.RequireAudiences("todo-api");
    })
    .WithEntityFrameworkStores<HuiaAppDbContext>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();

    // ASP.NET Core's default document transformer reports the server URL with a trailing slash (e.g.
    // "https://localhost:5041/"); every path in the document (including "/connect/authorize") already
    // starts with its own slash, so concatenating the two verbatim — which is exactly what Scalar's own
    // OAuth "Authorize" flow does to build the request it sends — produces a double slash
    // ("https://localhost:5041//connect/authorize"). ASP.NET Core's router treats that as a different,
    // non-existent path and 404s, so Scalar's "Authorize" button never actually reaches
    // AuthorizationEndpoints — silently reusing an existing Huia session (SSO) or showing its login page
    // both require the request to land there in the first place
    options.AddDocumentTransformer((document, _, _) =>
    {
        foreach (var server in document.Servers ?? [])
        {
            server.Url = server.Url?.TrimEnd('/');
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Known upstream bug in the bundled Scalar client (@scalar/api-reference, as of Scalar.AspNetCore
    // 2.16.16, the latest available): clicking "Clear" next to an acquired OAuth2 token wipes this flow's
    // Redirect URL field along with the token, but leaves every other field (Auth URL, Client ID, PKCE, ...)
    // alone — re-selecting "OAuth2" from the Auth Type dropdown doesn't restore it either. The next
    // "Authorize" click then submits an empty redirect_uri, which /connect/authorize correctly rejects with
    // a 400. Reloading the page after Clear (before Authorize again) re-reads this config fresh and fixes
    // it; there's no server-side workaround since the value only ever goes missing in the client's own
    // in-memory state.
    app.MapScalarApiReference(options => options
        .WithTitle("Huia Todo Sample API")
        .AddAuthorizationCodeFlow("OAuth2", flow =>
        {
            flow.ClientId = "scalar";
            flow.Pkce = Pkce.Sha256;
            flow.AuthorizationUrl = $"{issuer}/connect/authorize";
            flow.TokenUrl = $"{issuer}/connect/token";
            flow.RedirectUri = $"{issuer}/scalar/v1";
            flow.SelectedScopes = ["todos"];
        }));
}

using (var scope = app.Services.CreateScope())
{
    // Migrations, not EnsureCreatedAsync: EnsureCreatedAsync only checks whether the database itself
    // exists, so once HuiaAppDbContext's migration created the physical file, TodoDbContext's own call
    // would see it already there and silently skip creating the Todos table.
    await scope.ServiceProvider.GetRequiredService<HuiaAppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TodoDbContext>().Database.MigrateAsync();

    await scope.ServiceProvider.SeedAdminAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHuia();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Required for /connect/verify POST (device flow approval) — see DeviceEndpoints.cs's own doc comment.
app.UseAntiforgery();

app.MapHuiaConnectEndpoints();
app.MapHuiaManageEndpoints();
// Huia's built-in CRUD over applications/scopes/authorizations/users/roles — unauthenticated by default
// (see its own doc comment), so every route in the group is gated here behind both the "Admin" role seeded
// by SeedAdminAsync below and RequirePresenter: RequireRole alone isn't enough, since any client that
// requested the "roles" scope (see AllowScopes above) could otherwise mint a token for an Admin user and
// reach these endpoints. "admin-ui" is the admin console itself; "huia-cli" is also allowed since its whole
// purpose (see DeviceFlowTests) is letting a signed-in Admin drive this same API from the command line.
app.MapHuiaAdminEndpoints().RequireAuthorization(policy => policy
    .RequireRole("Admin")
    .RequirePresenter("admin-ui", "huia-cli"));

app.MapRazorPages();
app.MapTodoEndpoints();
app.MapReportsEndpoints();

app.Run();